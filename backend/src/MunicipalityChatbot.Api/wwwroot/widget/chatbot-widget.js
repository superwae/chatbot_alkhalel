(function() {
  'use strict';

  // Configuration - will be set by the loader script
  const config = window.MunicipalityChatbotConfig || {};
  const API_URL = config.apiUrl || '';
  const API_KEY = config.apiKey || '';
  const POSITION = config.position || 'bottom-right';
  const THEME_COLOR = config.themeColor || '#0066cc';
  const TITLE = config.title || 'Municipality Assistant';
  const TITLE_AR = config.titleAr || 'مساعد البلدية';
  const PLACEHOLDER = config.placeholder || 'Type your message...';
  const PLACEHOLDER_AR = config.placeholderAr || 'اكتب رسالتك...';
  const DEFAULT_LANG = config.defaultLang || 'ar';

  if (!API_URL) {
    console.error('MunicipalityChatbot: apiUrl is required in MunicipalityChatbotConfig');
    return;
  }

  // State
  let isOpen = false;
  let sessionId = null;
  let currentLang = DEFAULT_LANG;
  let isLoading = false;
  let userToken = config.userToken || null;
  let customerId = config.customerId || null;

  // Create styles
  const styles = document.createElement('style');
  styles.textContent = `
    .mcb-widget-container * {
      box-sizing: border-box;
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, sans-serif;
    }

    .mcb-widget-button {
      position: fixed;
      ${POSITION.includes('right') ? 'right: 20px' : 'left: 20px'};
      ${POSITION.includes('bottom') ? 'bottom: 20px' : 'top: 20px'};
      width: 60px;
      height: 60px;
      border-radius: 50%;
      background: ${THEME_COLOR};
      border: none;
      cursor: pointer;
      box-shadow: 0 4px 12px rgba(0,0,0,0.15);
      display: flex;
      align-items: center;
      justify-content: center;
      transition: transform 0.2s, box-shadow 0.2s;
      z-index: 999998;
    }

    .mcb-widget-button:hover {
      transform: scale(1.05);
      box-shadow: 0 6px 16px rgba(0,0,0,0.2);
    }

    .mcb-widget-button svg {
      width: 28px;
      height: 28px;
      fill: white;
    }

    .mcb-chat-window {
      position: fixed;
      ${POSITION.includes('right') ? 'right: 20px' : 'left: 20px'};
      ${POSITION.includes('bottom') ? 'bottom: 90px' : 'top: 90px'};
      width: 380px;
      height: 520px;
      max-height: calc(100vh - 120px);
      background: #fff;
      border-radius: 12px;
      box-shadow: 0 8px 32px rgba(0,0,0,0.15);
      display: none;
      flex-direction: column;
      overflow: hidden;
      z-index: 999999;
    }

    .mcb-chat-window.open {
      display: flex;
    }

    .mcb-chat-header {
      background: ${THEME_COLOR};
      color: white;
      padding: 16px;
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .mcb-chat-title {
      font-size: 16px;
      font-weight: 600;
      margin: 0;
    }

    .mcb-header-actions {
      display: flex;
      gap: 8px;
    }

    .mcb-header-btn {
      background: rgba(255,255,255,0.2);
      border: none;
      color: white;
      width: 32px;
      height: 32px;
      border-radius: 6px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 12px;
      transition: background 0.2s;
    }

    .mcb-header-btn:hover {
      background: rgba(255,255,255,0.3);
    }

    .mcb-messages {
      flex: 1;
      overflow-y: auto;
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 12px;
      background: #f5f5f5;
    }

    .mcb-message {
      max-width: 85%;
      padding: 10px 14px;
      border-radius: 12px;
      font-size: 14px;
      line-height: 1.5;
      word-wrap: break-word;
    }

    .mcb-message.user {
      align-self: flex-end;
      background: ${THEME_COLOR};
      color: white;
      border-bottom-right-radius: 4px;
    }

    .mcb-message.assistant {
      align-self: flex-start;
      background: white;
      color: #333;
      border-bottom-left-radius: 4px;
      box-shadow: 0 1px 2px rgba(0,0,0,0.1);
    }

    .mcb-message.assistant[dir="rtl"] {
      text-align: right;
    }

    .mcb-message.user[dir="rtl"] {
      text-align: right;
    }

    .mcb-typing {
      align-self: flex-start;
      background: white;
      padding: 12px 16px;
      border-radius: 12px;
      display: flex;
      gap: 4px;
    }

    .mcb-typing-dot {
      width: 8px;
      height: 8px;
      background: #999;
      border-radius: 50%;
      animation: mcb-typing 1.4s infinite;
    }

    .mcb-typing-dot:nth-child(2) { animation-delay: 0.2s; }
    .mcb-typing-dot:nth-child(3) { animation-delay: 0.4s; }

    @keyframes mcb-typing {
      0%, 60%, 100% { transform: translateY(0); }
      30% { transform: translateY(-4px); }
    }

    .mcb-input-area {
      padding: 12px;
      background: white;
      border-top: 1px solid #e0e0e0;
      display: flex;
      gap: 8px;
    }

    .mcb-input {
      flex: 1;
      border: 1px solid #ddd;
      border-radius: 20px;
      padding: 10px 16px;
      font-size: 14px;
      outline: none;
      transition: border-color 0.2s;
    }

    .mcb-input:focus {
      border-color: ${THEME_COLOR};
    }

    .mcb-input[dir="rtl"] {
      text-align: right;
    }

    .mcb-send-btn {
      background: ${THEME_COLOR};
      border: none;
      color: white;
      width: 40px;
      height: 40px;
      border-radius: 50%;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: background 0.2s, transform 0.2s;
    }

    .mcb-send-btn:hover:not(:disabled) {
      transform: scale(1.05);
    }

    .mcb-send-btn:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    .mcb-send-btn svg {
      width: 18px;
      height: 18px;
      fill: white;
    }

    .mcb-welcome {
      text-align: center;
      padding: 20px;
      color: #666;
    }

    .mcb-welcome-icon {
      font-size: 48px;
      margin-bottom: 12px;
    }

    .mcb-welcome h3 {
      margin: 0 0 8px 0;
      color: #333;
    }

    .mcb-welcome p {
      margin: 0;
      font-size: 14px;
    }

    @media (max-width: 420px) {
      .mcb-chat-window {
        width: calc(100vw - 20px);
        height: calc(100vh - 100px);
        ${POSITION.includes('right') ? 'right: 10px' : 'left: 10px'};
      }
    }
  `;
  document.head.appendChild(styles);

  // Create widget container
  const container = document.createElement('div');
  container.className = 'mcb-widget-container';

  // Chat button
  const button = document.createElement('button');
  button.className = 'mcb-widget-button';
  button.innerHTML = `
    <svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
      <path d="M20 2H4c-1.1 0-2 .9-2 2v18l4-4h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm0 14H5.17L4 17.17V4h16v12z"/>
      <path d="M7 9h10v2H7zm0-3h10v2H7z"/>
    </svg>
  `;
  button.setAttribute('aria-label', 'Open chat');

  // Chat window
  const chatWindow = document.createElement('div');
  chatWindow.className = 'mcb-chat-window';
  chatWindow.innerHTML = `
    <div class="mcb-chat-header">
      <h2 class="mcb-chat-title">${currentLang === 'ar' ? TITLE_AR : TITLE}</h2>
      <div class="mcb-header-actions">
        <button class="mcb-header-btn mcb-lang-toggle" title="Toggle Language">${currentLang === 'ar' ? 'EN' : 'AR'}</button>
        <button class="mcb-header-btn mcb-close-btn" title="Close">
          <svg width="14" height="14" viewBox="0 0 14 14" fill="currentColor">
            <path d="M14 1.41L12.59 0 7 5.59 1.41 0 0 1.41 5.59 7 0 12.59 1.41 14 7 8.41 12.59 14 14 12.59 8.41 7z"/>
          </svg>
        </button>
      </div>
    </div>
    <div class="mcb-messages">
      <div class="mcb-welcome">
        <div class="mcb-welcome-icon">👋</div>
        <h3>${currentLang === 'ar' ? 'مرحباً!' : 'Hello!'}</h3>
        <p>${currentLang === 'ar' ? 'كيف يمكنني مساعدتك اليوم؟' : 'How can I help you today?'}</p>
      </div>
    </div>
    <div class="mcb-input-area">
      <input type="text" class="mcb-input" placeholder="${currentLang === 'ar' ? PLACEHOLDER_AR : PLACEHOLDER}" dir="${currentLang === 'ar' ? 'rtl' : 'ltr'}">
      <button class="mcb-send-btn" disabled>
        <svg viewBox="0 0 24 24">
          <path d="M2.01 21L23 12 2.01 3 2 10l15 2-15 2z"/>
        </svg>
      </button>
    </div>
  `;

  container.appendChild(button);
  container.appendChild(chatWindow);
  document.body.appendChild(container);

  // Get references
  const messagesContainer = chatWindow.querySelector('.mcb-messages');
  const input = chatWindow.querySelector('.mcb-input');
  const sendBtn = chatWindow.querySelector('.mcb-send-btn');
  const closeBtn = chatWindow.querySelector('.mcb-close-btn');
  const langToggle = chatWindow.querySelector('.mcb-lang-toggle');
  const titleEl = chatWindow.querySelector('.mcb-chat-title');

  // Functions
  function toggleChat() {
    isOpen = !isOpen;
    chatWindow.classList.toggle('open', isOpen);
    if (isOpen) {
      input.focus();
    }
  }

  function toggleLanguage() {
    currentLang = currentLang === 'ar' ? 'en' : 'ar';
    langToggle.textContent = currentLang === 'ar' ? 'EN' : 'AR';
    titleEl.textContent = currentLang === 'ar' ? TITLE_AR : TITLE;
    input.placeholder = currentLang === 'ar' ? PLACEHOLDER_AR : PLACEHOLDER;
    input.dir = currentLang === 'ar' ? 'rtl' : 'ltr';
  }

  function detectLanguage(text) {
    // Simple Arabic detection
    const arabicPattern = /[\u0600-\u06FF]/;
    return arabicPattern.test(text) ? 'ar' : 'en';
  }

  function addMessage(text, role, lang) {
    // Remove welcome message if present
    const welcome = messagesContainer.querySelector('.mcb-welcome');
    if (welcome) {
      welcome.remove();
    }

    const msgEl = document.createElement('div');
    msgEl.className = `mcb-message ${role}`;
    // Convert URLs to clickable links, escape HTML first
    const escaped = text.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
    const linked = escaped.replace(/(https?:\/\/[^\s,،)}\]]+)/g, '<a href="$1" target="_blank" rel="noopener noreferrer" style="color:#0066cc;text-decoration:underline">$1</a>');
    msgEl.innerHTML = linked;

    const msgLang = lang || detectLanguage(text);
    msgEl.dir = msgLang === 'ar' ? 'rtl' : 'ltr';

    messagesContainer.appendChild(msgEl);
    messagesContainer.scrollTop = messagesContainer.scrollHeight;
  }

  function showTyping() {
    const typing = document.createElement('div');
    typing.className = 'mcb-typing';
    typing.innerHTML = '<div class="mcb-typing-dot"></div><div class="mcb-typing-dot"></div><div class="mcb-typing-dot"></div>';
    messagesContainer.appendChild(typing);
    messagesContainer.scrollTop = messagesContainer.scrollHeight;
    return typing;
  }

  async function sendMessage() {
    const text = input.value.trim();
    if (!text || isLoading) return;

    isLoading = true;
    input.value = '';
    sendBtn.disabled = true;

    addMessage(text, 'user');
    const typing = showTyping();

    try {
      const headers = {
        'Content-Type': 'application/json'
      };

      if (API_KEY) {
        headers['X-Widget-Api-Key'] = API_KEY;
      }

      const body = {
        message: text,
        lang: currentLang,
        sessionId: sessionId
      };
      if (userToken) {
        body.userToken = userToken;
      }
      if (customerId) {
        body.customerId = customerId;
      }

      const response = await fetch(`${API_URL}/api/chat/public`, {
        method: 'POST',
        headers: headers,
        body: JSON.stringify(body)
      });

      typing.remove();

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const data = await response.json();
      sessionId = data.sessionId;

      // Show answer or follow-up question
      const answer = data.followUpQuestion || data.answer;
      if (answer) {
        addMessage(answer, 'assistant');
      }
    } catch (error) {
      typing.remove();
      console.error('MunicipalityChatbot error:', error);
      const errorMsg = currentLang === 'ar'
        ? 'عذراً، حدث خطأ. يرجى المحاولة مرة أخرى.'
        : 'Sorry, an error occurred. Please try again.';
      addMessage(errorMsg, 'assistant');
    } finally {
      isLoading = false;
      sendBtn.disabled = false;
      input.focus();
    }
  }

  // Event listeners
  button.addEventListener('click', toggleChat);
  closeBtn.addEventListener('click', toggleChat);
  langToggle.addEventListener('click', toggleLanguage);

  sendBtn.addEventListener('click', sendMessage);

  input.addEventListener('keypress', (e) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  });

  input.addEventListener('input', () => {
    sendBtn.disabled = !input.value.trim() || isLoading;
  });

  // Expose API for programmatic control
  window.MunicipalityChatbot = {
    open: () => { if (!isOpen) toggleChat(); },
    close: () => { if (isOpen) toggleChat(); },
    toggle: toggleChat,
    setLanguage: (lang) => {
      if (lang === 'ar' || lang === 'en') {
        currentLang = lang === 'ar' ? 'en' : 'ar'; // Will be toggled
        toggleLanguage();
      }
    },
    sendMessage: (msg) => {
      input.value = msg;
      sendMessage();
    },
    setUserToken: (token) => {
      userToken = token || null;
    },
    setCustomerId: (id) => {
      customerId = id || null;
    }
  };

  console.log('MunicipalityChatbot widget loaded successfully');
})();
