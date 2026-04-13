let state = { page: 1, limit: 20, type: null, genre: null, search: '', favorites: false, total: 0, viewMode: 'grid' };
let editingId = null;

// ── Data loading ─────────────────────────────────────────────────────────────

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
    renderMedia(data.items);
    renderPagination();
    syncAllBtn();
  } catch {
    grid.textContent = '';
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'Failed to load. Check connection.';
    grid.appendChild(msg);
  }
}

// ── Rendering — dispatches to grid or list mode ──────────────────────────────

function renderMedia(items) {
  if (state.viewMode === 'list') renderTable(items);
  else renderCards(items);
}

// Grid (poster card) view
function renderCards(items) {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  grid.classList.remove('list-mode');

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
    img.alt = item.title;
    img.loading = 'lazy';
    img.onerror = () => { img.onerror = null; img.src = '/img/no-poster.svg'; };

    // Favorite toggle — top-left on hover
    const favBtn = document.createElement('button');
    favBtn.className = 'card-fav' + (item.isFavorite ? ' fav-active' : '');
    favBtn.textContent = item.isFavorite ? '♥' : '♡';
    favBtn.title = item.isFavorite ? 'Remove from favorites' : 'Add to favorites';
    favBtn.addEventListener('click', e => { e.stopPropagation(); toggleFav(item.id); });

    // Edit overlay — top-right on hover
    const editBtn = document.createElement('button');
    editBtn.className = 'card-edit';
    editBtn.textContent = '✏';
    editBtn.title = 'Edit / Classify';
    editBtn.addEventListener('click', e => { e.stopPropagation(); openEditModal(item); });

    const info = document.createElement('div');
    info.className = 'info';

    const titleEl = document.createElement('div');
    titleEl.className = 'card-title';
    titleEl.textContent = item.title;                  // textContent — XSS safe

    const metaEl = document.createElement('div');
    metaEl.className = 'meta';
    metaEl.textContent = [item.year, item.type].filter(Boolean).join(' · ');

    info.appendChild(titleEl);
    info.appendChild(metaEl);

    if (item.isFavorite) {
      const fav = document.createElement('div');
      fav.className = 'fav';
      fav.textContent = '♥ Favorite';
      info.appendChild(fav);
    }

    card.appendChild(img);
    card.appendChild(favBtn);
    card.appendChild(editBtn);
    card.appendChild(info);
    grid.appendChild(card);
  });
}

// List (table) view — full metadata, Play / Edit / Fav / Delete per row
function renderTable(items) {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  grid.classList.add('list-mode');

  if (!items.length) {
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'No media found.';
    grid.appendChild(msg);
    return;
  }

  const table = document.createElement('table');
  table.className = 'media-table';

  // Header
  const headerRow = table.createTHead().insertRow();
  ['Title', 'Type', 'Year', 'Genres', 'Rating', 'Actions'].forEach(col => {
    const th = document.createElement('th');
    th.textContent = col;
    headerRow.appendChild(th);
  });

  // Body
  const tbody = table.createTBody();
  items.forEach(item => {
    const row = tbody.insertRow();

    const titleCell = row.insertCell();
    titleCell.className = 'cell-title';
    titleCell.textContent = item.title;                // textContent — XSS safe
    titleCell.title = item.title;

    const typeCell = row.insertCell();
    typeCell.textContent = item.type || '—';

    const yearCell = row.insertCell();
    yearCell.textContent = item.year || '—';

    const genresCell = row.insertCell();
    genresCell.className = 'cell-genres';
    genresCell.textContent = item.genres || '—';
    genresCell.title = item.genres || '';

    const ratingCell = row.insertCell();
    ratingCell.textContent = item.rating != null ? item.rating.toFixed(1) : '—';

    const actionsCell = row.insertCell();
    actionsCell.className = 'cell-actions';

    const playBtn = document.createElement('button');
    playBtn.className = 'filter-btn';
    playBtn.textContent = '▶ Play';
    playBtn.addEventListener('click', () => openPlayer(item.id));

    const editBtn = document.createElement('button');
    editBtn.className = 'filter-btn';
    editBtn.textContent = '✏ Edit';
    editBtn.addEventListener('click', () => openEditModal(item));

    const favBtn = document.createElement('button');
    favBtn.className = 'filter-btn' + (item.isFavorite ? ' active' : '');
    favBtn.textContent = item.isFavorite ? '♥' : '♡';
    favBtn.title = item.isFavorite ? 'Remove from favorites' : 'Add to favorites';
    favBtn.addEventListener('click', () => toggleFav(item.id));

    const delBtn = document.createElement('button');
    delBtn.className = 'filter-btn btn-danger';
    delBtn.textContent = '🗑';
    delBtn.title = 'Remove from library (file stays on disk)';
    delBtn.addEventListener('click', () => {
      if (confirm(`Delete "${item.title}" from the library?\n\nThe file on disk will NOT be removed.`))
        deleteById(item.id);
    });

    actionsCell.appendChild(playBtn);
    actionsCell.appendChild(editBtn);
    actionsCell.appendChild(favBtn);
    actionsCell.appendChild(delBtn);
  });

  grid.appendChild(table);
}

// ── Edit / Classify modal ────────────────────────────────────────────────────

function openEditModal(item) {
  editingId = item.id;
  document.getElementById('eTitle').value   = item.title        || '';
  document.getElementById('eType').value    = item.type         || 'Movie';
  document.getElementById('eYear').value    = item.year         ?? '';
  document.getElementById('eRating').value  = item.rating       ?? '';
  document.getElementById('eGenres').value  = item.genres       || '';
  document.getElementById('eDesc').value    = item.description  || '';
  document.getElementById('editModal').showModal();
}

function closeEditModal() {
  document.getElementById('editModal').close();
  editingId = null;
}

async function saveMedia() {
  if (!editingId) return;

  const yearRaw   = document.getElementById('eYear').value;
  const ratingRaw = document.getElementById('eRating').value;

  const body = {
    title:       document.getElementById('eTitle').value.trim()  || undefined,
    type:        document.getElementById('eType').value          || undefined,
    year:        yearRaw   ? parseInt(yearRaw, 10)  : undefined,
    rating:      ratingRaw ? parseFloat(ratingRaw) : undefined,
    genres:      document.getElementById('eGenres').value.trim() || undefined,
    description: document.getElementById('eDesc').value.trim()  || undefined,
  };

  try {
    await API.put(`/api/media/${editingId}`, body);
    closeEditModal();
    showToast('Media updated.');
    loadMedia();
  } catch {
    showToast('Update failed.');
  }
}

async function deleteMedia() {
  if (!editingId) return;
  if (!confirm('Remove this entry from the library?\n\nThe file on disk will NOT be deleted.')) return;
  closeEditModal();
  await deleteById(editingId);
}

async function deleteById(id) {
  try {
    await API.del(`/api/media/${id}`);
    showToast('Removed from library.');
    loadMedia();
  } catch {
    showToast('Delete failed.');
  }
}

async function toggleFav(id) {
  try {
    await API.post(`/api/media/${id}/favorite`, {});
    loadMedia();
  } catch {
    showToast('Failed to update favorite.');
  }
}

// Close when clicking outside (backdrop click)
document.getElementById('editModal').addEventListener('click', e => {
  if (e.target === e.currentTarget) e.currentTarget.close();
});

// ── Add Media modal ──────────────────────────────────────────────────────────

function openAddModal() {
  document.getElementById('aFilePath').value = '';
  document.getElementById('aTitle').value    = '';
  document.getElementById('aType').value     = 'Movie';
  document.getElementById('aYear').value     = '';
  document.getElementById('addModal').showModal();
}

function closeAddModal() {
  document.getElementById('addModal').close();
}

// Auto-fill title from the filename portion of the path typed so far
function autofillTitle() {
  const fp = document.getElementById('aFilePath').value.trim();
  const filename = fp.split(/[\\/]/).pop() || '';
  // Strip extension, replace common separators with spaces: film.dark.knight → film dark knight
  const name = filename.replace(/\.[^.]+$/, '').replace(/[._-]+/g, ' ').trim();
  if (name) document.getElementById('aTitle').value = name;
}

async function saveNewMedia() {
  const filePath = document.getElementById('aFilePath').value.trim();
  if (!filePath) { showToast('File path is required.'); return; }

  const yearRaw = document.getElementById('aYear').value;
  const body = {
    filePath,
    title: document.getElementById('aTitle').value.trim() || undefined,
    type:  document.getElementById('aType').value,
    year:  yearRaw ? parseInt(yearRaw, 10) : undefined,
  };

  try {
    await API.post('/api/media', body);
    closeAddModal();
    showToast('Added to library.');
    loadMedia();
  } catch (e) {
    showToast('Failed: ' + e.message);
  }
}

document.getElementById('addModal').addEventListener('click', e => {
  if (e.target === e.currentTarget) e.currentTarget.close();
});

// ── Pagination ───────────────────────────────────────────────────────────────

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
  label.textContent = `${state.page} / ${totalPages}  (${state.total} items)`;

  const next = document.createElement('button');
  next.textContent = 'Next →';
  next.disabled = state.page >= totalPages;
  next.addEventListener('click', () => changePage(state.page + 1));

  el.appendChild(prev);
  el.appendChild(label);
  el.appendChild(next);
}

// ── Controls ─────────────────────────────────────────────────────────────────

function changePage(p) { state.page = p; loadMedia(); }
function openPlayer(id) { location.href = `/player.html?id=${id}`; }

// "All" button — clears every filter and shows the full library
function clearFilters() {
  state.type = null;
  state.favorites = false;
  state.search = '';
  state.page = 1;
  document.getElementById('searchInput').value = '';
  document.querySelectorAll('.filter-btn[data-type]').forEach(b => b.classList.remove('active'));
  document.getElementById('favBtn').classList.remove('active');
  loadMedia();   // syncAllBtn() is called inside loadMedia after data arrives
}

// "All" button is active when no type filter and not in favorites-only mode
function syncAllBtn() {
  document.getElementById('allBtn').classList.toggle('active', !state.type && !state.favorites);
}

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

function toggleView() {
  state.viewMode = state.viewMode === 'grid' ? 'list' : 'grid';
  const btn = document.getElementById('viewBtn');
  btn.textContent = state.viewMode === 'grid' ? '☰ List' : '⊞ Grid';
  btn.classList.toggle('active', state.viewMode === 'list');
  loadMedia();
}

document.getElementById('searchInput').addEventListener('input', e => {
  state.search = e.target.value;
  state.page = 1;
  clearTimeout(window._searchTimer);
  window._searchTimer = setTimeout(loadMedia, 400);
});

// ── Scan ─────────────────────────────────────────────────────────────────────

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

// ── Toast ─────────────────────────────────────────────────────────────────────

function showToast(msg) {
  const t = document.getElementById('toast');
  t.textContent = msg;                               // textContent — XSS safe
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 3000);
}

loadMedia();
