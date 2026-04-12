let state = { page: 1, limit: 20, type: null, genre: null, search: '', favorites: false, total: 0 };

async function loadMedia() {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  const spinner = document.createElement('div');
  spinner.className = 'spinner';
  spinner.textContent = 'Loading…';
  grid.appendChild(spinner);

  const params = new URLSearchParams({ page: state.page, limit: state.limit });
  if (state.type)      params.set('type', state.type);
  if (state.genre)     params.set('genre', state.genre);
  if (state.search)    params.set('search', state.search);
  if (state.favorites) params.set('favorites', 'true');

  try {
    const data = await API.get('/api/media?' + params);
    state.total = data.total;
    renderGrid(data.items);
    renderPagination();
  } catch {
    grid.textContent = '';
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'Failed to load. Check connection.';
    grid.appendChild(msg);
  }
}

function renderGrid(items) {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  if (!items.length) {
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'No media found.';
    grid.appendChild(msg);
    return;
  }
  items.forEach(item => {
    const card = document.createElement('div');
    card.className = 'card';
    card.setAttribute('role', 'listitem');
    card.addEventListener('click', () => openPlayer(item.id));

    const img = document.createElement('img');
    img.src = item.posterPath || '/img/no-poster.svg';
    img.alt = item.title;   // alt text from server is safe for img.alt
    img.loading = 'lazy';
    img.onerror = () => { img.src = '/img/no-poster.svg'; };

    const info = document.createElement('div');
    info.className = 'info';

    const titleEl = document.createElement('div');
    titleEl.className = 'card-title';
    titleEl.textContent = item.title;           // textContent — XSS safe

    const metaEl = document.createElement('div');
    metaEl.className = 'meta';
    metaEl.textContent = [item.year, item.type].filter(Boolean).join(' · ');

    info.appendChild(titleEl);
    info.appendChild(metaEl);

    if (item.isFavorite) {
      const fav = document.createElement('div');
      fav.className = 'fav';
      fav.textContent = 'Favorite';
      info.appendChild(fav);
    }

    card.appendChild(img);
    card.appendChild(info);
    grid.appendChild(card);
  });
}

function renderPagination() {
  const totalPages = Math.ceil(state.total / state.limit) || 1;
  const el = document.getElementById('pagination');
  el.textContent = '';

  const prev = document.createElement('button');
  prev.textContent = '← Prev';
  prev.disabled = state.page <= 1;
  prev.addEventListener('click', () => changePage(state.page - 1));

  const label = document.createElement('span');
  label.style.cssText = 'padding:6px 12px;color:var(--text2)';
  label.textContent = `${state.page} / ${totalPages}`;

  const next = document.createElement('button');
  next.textContent = 'Next →';
  next.disabled = state.page >= totalPages;
  next.addEventListener('click', () => changePage(state.page + 1));

  el.appendChild(prev);
  el.appendChild(label);
  el.appendChild(next);
}

function changePage(p) { state.page = p; loadMedia(); }
function openPlayer(id) { location.href = `/player.html?id=${id}`; }

function setFilter(type) {
  state.type = state.type === type ? null : type;
  state.page = 1;
  document.querySelectorAll('.filter-btn[data-type]').forEach(b =>
    b.classList.toggle('active', b.dataset.type === state.type));
  loadMedia();
}

function toggleFavorites() {
  state.favorites = !state.favorites;
  state.page = 1;
  document.getElementById('favBtn').classList.toggle('active', state.favorites);
  loadMedia();
}

document.getElementById('searchInput').addEventListener('input', e => {
  state.search = e.target.value;
  state.page = 1;
  clearTimeout(window._searchTimer);
  window._searchTimer = setTimeout(loadMedia, 400);
});

async function triggerScan() {
  const btn = document.getElementById('scanBtn');
  btn.textContent = 'Scanning…';
  btn.disabled = true;
  try {
    await API.post('/api/media/scan', {});
    showToast('Scan started!');
  } catch (e) {
    showToast('Error: ' + e.message);
  } finally {
    setTimeout(() => { btn.textContent = 'Scan Library'; btn.disabled = false; }, 3000);
  }
}

function showToast(msg) {
  const t = document.getElementById('toast');
  t.textContent = msg;   // textContent — XSS safe
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 3000);
}

loadMedia();
