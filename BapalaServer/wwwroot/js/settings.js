let settings = {};

async function load() {
  settings = await API.get('/api/settings');
  renderFolders();

  // Safe DOM writes — no innerHTML for server data
  const info = document.getElementById('serverInfo');
  info.textContent = '';
  [
    ['Server', settings.serverName],
    ['Port', settings.port],
    ['TMDB', settings.hasTmdbKey ? 'Configured' : 'Not set'],
    ['User', settings.username],
  ].forEach(([label, value]) => {
    const row = document.createElement('div');
    const b = document.createElement('b');
    b.textContent = String(value);          // textContent — XSS safe
    row.textContent = `${label}: `;
    row.appendChild(b);
    info.appendChild(row);
  });
}

function renderFolders() {
  const list = document.getElementById('folderList');
  list.textContent = '';
  const folders = settings.mediaFolders || [];
  if (!folders.length) {
    const li = document.createElement('li');
    li.style.color = 'var(--text2)';
    li.textContent = 'No folders added yet.';
    list.appendChild(li);
    return;
  }
  folders.forEach(f => {
    const li = document.createElement('li');
    const span = document.createElement('span');
    span.textContent = f;                    // textContent — XSS safe
    const btn = document.createElement('button');
    btn.textContent = 'Remove';
    btn.addEventListener('click', () => removeFolder(f));
    li.appendChild(span);
    li.appendChild(btn);
    list.appendChild(li);
  });
}

function addFolder() {
  const val = document.getElementById('newFolder').value.trim();
  if (!val) return;
  settings.mediaFolders = settings.mediaFolders || [];
  settings.mediaFolders.push(val);
  document.getElementById('newFolder').value = '';
  renderFolders();
}

function removeFolder(f) {
  settings.mediaFolders = settings.mediaFolders.filter(x => x !== f);
  renderFolders();
}

async function saveSettings() {
  try {
    await API.put('/api/settings', {
      mediaFolders: settings.mediaFolders,
      tmdbApiKey: document.getElementById('tmdbKey').value || undefined
    });
    showToast('Saved. Restart the server to apply changes.');
  } catch { showToast('Save failed.'); }
}

function logout() { localStorage.removeItem('bapala_token'); location.href = '/login.html'; }

function showToast(msg) {
  const t = document.getElementById('toast');
  t.textContent = msg;
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 3500);
}

load();
