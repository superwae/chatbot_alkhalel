# Embeddable Chat Widget

> Allow external websites to embed the municipality chatbot

## Overview

The chatbot can be embedded on any website using a simple JavaScript widget. The widget:
- Creates a floating chat button
- Opens a chat window when clicked
- Supports Arabic and English with RTL layout
- Calls the backend public chat API

## Widget Files

| File | Location | Description |
|------|----------|-------------|
| `chatbot-widget.js` | `backend/.../wwwroot/widget/` | Main widget script |
| `example.html` | `backend/.../wwwroot/widget/` | Example usage page |

---

## Embedding the Widget

Add this code before the closing `</body>` tag on the client's website:

```html
<script>
  window.MunicipalityChatbotConfig = {
    apiUrl: 'https://YOUR_SERVER_URL',
    // apiKey: 'optional-widget-api-key',
    position: 'bottom-right',  // or 'bottom-left'
    themeColor: '#0066cc',
    defaultLang: 'ar',         // 'ar' or 'en'
    title: 'Municipality Assistant',
    titleAr: 'مساعد البلدية',
    placeholder: 'Type your message...',
    placeholderAr: 'اكتب رسالتك...'
  };
</script>
<script src="https://YOUR_SERVER_URL/widget/chatbot-widget.js"></script>
```

---

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `apiUrl` | string | (required) | Backend server URL |
| `apiKey` | string | - | Widget API key if server requires it |
| `position` | string | `'bottom-right'` | `'bottom-right'` or `'bottom-left'` |
| `themeColor` | string | `'#0066cc'` | Primary color for the widget |
| `defaultLang` | string | `'ar'` | Initial language (`'ar'` or `'en'`) |
| `title` | string | `'Municipality Assistant'` | English title |
| `titleAr` | string | `'مساعد البلدية'` | Arabic title |
| `placeholder` | string | `'Type your message...'` | English input placeholder |
| `placeholderAr` | string | `'اكتب رسالتك...'` | Arabic input placeholder |

---

## JavaScript API

After the widget loads, you can control it programmatically:

```javascript
// Open the chat window
MunicipalityChatbot.open();

// Close the chat window
MunicipalityChatbot.close();

// Toggle chat window
MunicipalityChatbot.toggle();

// Change language
MunicipalityChatbot.setLanguage('ar');  // or 'en'

// Send a message programmatically
MunicipalityChatbot.sendMessage('ما هي ساعات العمل؟');
```

---

## Server Configuration

### CORS Settings

The server must allow requests from the widget domain:

```bash
# Allow specific domains (comma-separated)
Cors__WidgetAllowedOrigins=https://client-site.com,https://another-site.com

# Or allow all domains (for testing only)
Cors__WidgetAllowedOrigins=*
```

### Optional: Require API Key

For added security, require widgets to include an API key:

```bash
# In server .env
WIDGET__API_KEY=your-secret-widget-key
```

Then include the key in the widget config:

```javascript
window.MunicipalityChatbotConfig = {
  apiUrl: 'https://YOUR_SERVER_URL',
  apiKey: 'your-secret-widget-key',  // Required if server enforces it
  // ... other options
};
```

---

## Widget Features

- **Floating Button**: Always visible in the corner of the page
- **Responsive**: Adapts to mobile screens
- **Language Toggle**: Users can switch between Arabic and English
- **RTL Support**: Arabic text displays right-to-left
- **Typing Indicator**: Shows when bot is processing
- **Session Persistence**: Maintains conversation across messages
- **Error Handling**: Shows friendly error messages

---

## Styling the Widget

The widget uses CSS classes prefixed with `mcb-` (Municipality ChatBot). You can override styles:

```css
/* Custom theme color override */
.mcb-widget-button {
  background: #your-color !important;
}

/* Larger chat window */
.mcb-chat-window {
  width: 420px !important;
  height: 600px !important;
}
```

---

## Testing Locally

1. Start the backend:
   ```bash
   docker compose up -d backend
   ```

2. Open the example page:
   ```
   http://localhost:8080/widget/example.html
   ```

3. Or create a test HTML file:
   ```html
   <!DOCTYPE html>
   <html>
   <head>
     <title>Widget Test</title>
   </head>
   <body>
     <h1>My Website</h1>
     <p>The chat widget should appear in the corner.</p>

     <script>
       window.MunicipalityChatbotConfig = {
         apiUrl: 'http://localhost:8080'
       };
     </script>
     <script src="http://localhost:8080/widget/chatbot-widget.js"></script>
   </body>
   </html>
   ```

---

## Troubleshooting

### Widget doesn't appear
- Check browser console for errors
- Verify `apiUrl` is correct and accessible
- Check CORS settings on server

### "Error occurred" message
- Backend may be down or unreachable
- Check backend logs: `docker logs municipality-chatbot-backend-1`
- Verify API key if required

### Arabic text not displaying correctly
- Ensure the host page has proper UTF-8 encoding:
  ```html
  <meta charset="UTF-8">
  ```

