#!/usr/bin/env python3
"""
Website crawler using Crawl4AI — replaces custom .NET BFS crawler.

Crawls a website using a headless browser (discovers JavaScript menus),
then pushes extracted content to the .NET backend for chunking, embedding,
and storage in PostgreSQL + Qdrant.

Usage:
    # Dry run (discover URLs only, no backend calls):
    python crawl_website.py --dry-run

    # Full crawl with backend ingestion:
    python crawl_website.py --backend-url http://localhost:8080 --token <JWT>

    # Crawl a different site:
    python crawl_website.py --target-url https://example.com --dry-run
"""

import argparse
import asyncio
import sys
import time
from urllib.parse import urlparse

import httpx
from crawl4ai import AsyncWebCrawler, BrowserConfig, CrawlerRunConfig
from crawl4ai.content_scraping_strategy import LXMLWebScrapingStrategy
from crawl4ai.deep_crawling import BFSDeepCrawlStrategy
from crawl4ai.deep_crawling.filters import DomainFilter, FilterChain

# File extensions to skip
SKIP_EXTENSIONS = {
    ".jpg", ".jpeg", ".png", ".gif", ".svg", ".ico", ".webp", ".bmp",
    ".mp3", ".mp4", ".avi", ".mov", ".wmv", ".flv",
    ".css", ".js", ".woff", ".woff2", ".ttf", ".eot",
    ".zip", ".rar", ".7z", ".tar", ".gz",
    ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
}


def should_skip_url(url: str) -> bool:
    """Check if URL should be skipped based on extension."""
    parsed = urlparse(url)
    path = parsed.path.lower()
    return any(path.endswith(ext) for ext in SKIP_EXTENSIONS)


def is_pdf_url(url: str) -> bool:
    """Check if URL points to a PDF file."""
    return urlparse(url).path.lower().endswith(".pdf")


async def ingest_page(
    client: httpx.AsyncClient,
    backend_url: str,
    headers: dict,
    url: str,
    title: str,
    text: str,
) -> dict:
    """Send crawled page content to backend for processing."""
    resp = await client.post(
        f"{backend_url}/api/website-crawl/ingest-page",
        json={"url": url, "title": title, "text": text},
        headers=headers,
        timeout=120,
    )
    resp.raise_for_status()
    return resp.json()


async def ingest_pdf(
    client: httpx.AsyncClient,
    backend_url: str,
    headers: dict,
    url: str,
    pdf_bytes: bytes,
) -> dict:
    """Send PDF file to backend for OCR processing."""
    filename = urlparse(url).path.split("/")[-1] or "document.pdf"
    resp = await client.post(
        f"{backend_url}/api/website-crawl/ingest-pdf",
        data={"url": url},
        files={"file": (filename, pdf_bytes, "application/pdf")},
        headers=headers,
        timeout=300,  # OCR can be slow for large PDFs
    )
    resp.raise_for_status()
    return resp.json()


async def finalize_crawl(
    client: httpx.AsyncClient,
    backend_url: str,
    headers: dict,
    crawled_urls: list[str],
) -> dict:
    """Tell backend to remove pages that no longer exist."""
    resp = await client.post(
        f"{backend_url}/api/website-crawl/finalize",
        json={"crawledUrls": crawled_urls},
        headers=headers,
        timeout=60,
    )
    resp.raise_for_status()
    return resp.json()


async def main(args: argparse.Namespace) -> None:
    parsed_target = urlparse(args.target_url)
    domain = parsed_target.hostname or ""
    # Allow both www and non-www variants
    domains = [domain]
    if domain.startswith("www."):
        domains.append(domain[4:])
    else:
        domains.append(f"www.{domain}")

    print(f"Target: {args.target_url}")
    print(f"Domains: {', '.join(domains)}")
    print(f"Max depth: {args.max_depth}, Max pages: {args.max_pages}")
    print(f"Dry run: {args.dry_run}")
    print()

    browser_config = BrowserConfig(
        headless=True,
        verbose=False,
    )

    filter_chain = FilterChain([
        DomainFilter(allowed_domains=domains),
    ])

    crawl_strategy = BFSDeepCrawlStrategy(
        max_depth=args.max_depth,
        max_pages=args.max_pages,
        include_external=False,
        filter_chain=filter_chain,
    )

    run_config = CrawlerRunConfig(
        deep_crawl_strategy=crawl_strategy,
        scraping_strategy=LXMLWebScrapingStrategy(),
        stream=True,
        verbose=False,
    )

    # Stats
    total = 0
    processed = 0
    skipped = 0
    pdfs_found = 0
    failed = 0
    crawled_urls: list[str] = []

    headers = {}
    if args.token:
        headers["Authorization"] = f"Bearer {args.token}"

    start_time = time.time()

    async with AsyncWebCrawler(config=browser_config) as crawler:
        client = httpx.AsyncClient() if not args.dry_run else None
        try:
            async for result in await crawler.arun(args.target_url, config=run_config):
                total += 1
                url = result.url

                if not result.success:
                    failed += 1
                    print(f"  [{total}] FAILED: {url}")
                    if result.error_message:
                        print(f"         Error: {result.error_message}")
                    continue

                # Skip non-content URLs
                if should_skip_url(url):
                    skipped += 1
                    print(f"  [{total}] SKIP (ext): {url}")
                    continue

                title = result.metadata.get("title", "") if result.metadata else ""
                depth = result.metadata.get("depth", 0) if result.metadata else 0
                content = result.markdown or ""

                if is_pdf_url(url):
                    pdfs_found += 1
                    crawled_urls.append(url)
                    if args.dry_run:
                        print(f"  [{total}] PDF (d={depth}): [{title or 'PDF'}] {url}")
                    else:
                        print(f"  [{total}] PDF (d={depth}): [{title or 'PDF'}] {url}")
                        # Download PDF and send to backend
                        try:
                            pdf_resp = await client.get(url, timeout=60, follow_redirects=True)
                            if pdf_resp.status_code == 200:
                                result_data = await ingest_pdf(
                                    client, args.backend_url, headers, url, pdf_resp.content
                                )
                                if result_data.get("processed"):
                                    processed += 1
                                    print(f"         -> {result_data.get('chunkCount', 0)} chunks")
                                else:
                                    skipped += 1
                                    print(f"         -> Skipped: {result_data.get('reason', '?')}")
                            else:
                                failed += 1
                                print(f"         -> Download failed: HTTP {pdf_resp.status_code}")
                        except Exception as e:
                            failed += 1
                            print(f"         -> Error: {e}")
                    continue

                # Regular HTML page
                if len(content.strip()) < 50:
                    skipped += 1
                    print(f"  [{total}] SKIP (short={len(content)}): {url}")
                    continue

                crawled_urls.append(url)

                if args.dry_run:
                    processed += 1
                    word_count = len(content.split())
                    print(f"  [{total}] OK (d={depth}, ~{word_count}w): [{title}] {url}")
                else:
                    try:
                        result_data = await ingest_page(
                            client, args.backend_url, headers, url, title, content
                        )
                        if result_data.get("processed"):
                            processed += 1
                            print(
                                f"  [{total}] OK (d={depth}): [{title}] {url}"
                                f" -> {result_data.get('chunkCount', 0)} chunks"
                            )
                        else:
                            skipped += 1
                            reason = result_data.get("reason", "?")
                            print(f"  [{total}] SKIP: [{title}] {url} -> {reason}")
                    except Exception as e:
                        failed += 1
                        print(f"  [{total}] ERROR: {url} -> {e}")

                if args.delay > 0:
                    await asyncio.sleep(args.delay)

        finally:
            if client:
                # Finalize: prune deleted pages
                if crawled_urls and not args.dry_run:
                    try:
                        print("\nFinalizing crawl (pruning deleted pages)...")
                        fin = await finalize_crawl(
                            client, args.backend_url, headers, crawled_urls
                        )
                        removed = fin.get("removedPages", 0)
                        if removed > 0:
                            print(f"  Removed {removed} orphaned pages")
                    except Exception as e:
                        print(f"  Finalize error: {e}")

                await client.aclose()

    elapsed = time.time() - start_time
    print()
    print("=" * 60)
    print(f"Crawl complete in {elapsed:.1f}s")
    print(f"  Total discovered: {total}")
    print(f"  Processed:        {processed}")
    print(f"  Skipped:          {skipped}")
    print(f"  PDFs found:       {pdfs_found}")
    print(f"  Failed:           {failed}")
    print(f"  URLs collected:   {len(crawled_urls)}")
    print("=" * 60)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Crawl a website using Crawl4AI and push content to the chatbot backend."
    )
    parser.add_argument(
        "--backend-url",
        default="http://localhost:8080",
        help="Base URL of the .NET backend (default: http://localhost:8080)",
    )
    parser.add_argument(
        "--target-url",
        default="https://www.hebron-city.ps",
        help="Website URL to crawl (default: https://www.hebron-city.ps)",
    )
    parser.add_argument(
        "--max-depth",
        type=int,
        default=3,
        help="Maximum crawl depth (default: 3)",
    )
    parser.add_argument(
        "--max-pages",
        type=int,
        default=500,
        help="Maximum pages to crawl (default: 500)",
    )
    parser.add_argument(
        "--token",
        default=None,
        help="JWT token for backend authentication",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Discover URLs only, don't push to backend",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0.5,
        help="Delay between page requests in seconds (default: 0.5)",
    )
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()

    if not args.dry_run and not args.token:
        print("ERROR: --token is required when not in --dry-run mode")
        sys.exit(1)

    asyncio.run(main(args))
