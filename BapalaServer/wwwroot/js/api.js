// SECURITY: All server-supplied content must use textContent, not innerHTML.
// Use esc() only as a last resort when building HTML strings.
const esc = s => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');

const API = {
  getToken: () => localStorage.getItem('bapala_token'),

  async request(path, options = {}) {
    const token = API.getToken();
    const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
    if (token) headers['Authorization'] = `Bearer ${token}`;

    const resp = await fetch(path, { ...options, headers });
    if (resp.status === 401) { localStorage.removeItem('bapala_token'); location.href = '/login.html'; }
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
    if (resp.status === 204) return null;
    return resp.json();
  },

  get:  path        => API.request(path),
  post: (path, body) => API.request(path, { method: 'POST', body: JSON.stringify(body) }),
  put:  (path, body) => API.request(path, { method: 'PUT',  body: JSON.stringify(body) }),
  del:  path        => API.request(path, { method: 'DELETE' }),
  streamUrl: id     => `/api/stream/${id}?token=${encodeURIComponent(API.getToken() || '')}`,
};

// Redirect to login if unauthenticated on any protected page
if (!document.querySelector('.login-wrap') && !API.getToken()) {
  location.href = '/login.html';
}
