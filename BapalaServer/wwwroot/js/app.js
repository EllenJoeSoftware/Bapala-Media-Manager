// ── Persisted preferences ─────────────────────────────────────────────────────
function loadPrefs() {
  try { return JSON.parse(localStorage.getItem('bapala_prefs') || '{}'); } catch { return {}; }
}
function savePrefs() {
  localStorage.setItem('bapala_prefs', JSON.stringify({
    type:     state.type,
    viewMode: state.viewMode,
    sortBy:   state.sortBy,
    sortDesc: state.sortDesc,
  }));
}

const _prefs = loadPrefs();
let state = {
  page: 1, limit: 20,
  type:       _prefs.type     ?? null,
  genre: null, search: '', favorites: false, total: 0,
  viewMode:   _prefs.viewMode ?? 'grid',
  sortBy:     _prefs.sortBy   ?? 'dateAdded',
  sortDesc:   _prefs.sortDesc ?? true,
  // series/course drill-down
  drillGroup: null,   // { name, type } — null means top-level
};
let editingId = null;
let selectMode = false;
let selectedIds = new Set();

// ── Types that support grouping ───────────────────────────────────────────────
const GROUPED_TYPES = ['Series', 'Education'];

function isGroupedView() {
  return GROUPED_TYPES.includes(state.type) && !state.drillGroup && !state.search;
}

// ── Data loading ──────────────────────────────────────────────────────────────

async function loadMedia() {
  // If we're in a grouped section (Series / Education) and not drilling down,
  // load the groups shelf instead of individual items.
  if (isGroupedView()) {
    await loadGroups();
    return;
  }

  const grid = document.getElementById('grid');
  grid.textContent = '';
  const spinner = document.createElement('div');
  spinner.className = 'spinner';
  spinner.textContent = 'Loading…';
  grid.appendChild(spinner);

  // If drilling into a series, fetch episodes via the dedicated endpoint
  if (state.drillGroup) {
    await loadSeriesEpisodes(state.drillGroup.name);
    return;
  }

  const params = new URLSearchParams({ page: state.page, limit: state.limit });
  if (state.type)      params.set('type', state.type);
  if (state.genre)     params.set('genre', state.genre);
  if (state.search)    params.set('search', state.search);
  if (state.favorites) params.set('favorites', 'true');
  params.set('sortBy', state.sortBy);
  params.set('sortDesc', state.sortDesc);

  try {
    const data = await API.get('/api/media?' + params);
    state.total = data.total;
    renderMedia(data.items);
    renderPagination();
    syncAllBtn();
    syncSortButtons();
  } catch {
    grid.textContent = '';
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'Failed to load. Check connection.';
    grid.appendChild(msg);
  }
}

// ── Groups shelf (Series / Education overview) ────────────────────────────────

async function loadGroups() {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  document.getElementById('pagination').textContent = '';

  const spinner = document.createElement('div');
  spinner.className = 'spinner';
  spinner.textContent = 'Loading…';
  grid.appendChild(spinner);

  try {
    const groups = await API.get(`/api/media/groups?type=${state.type}`);
    renderGroups(groups);
  } catch {
    grid.textContent = '';
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'Failed to load.';
    grid.appendChild(msg);
  }
}

function renderGroups(groups) {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  grid.classList.remove('list-mode');

  if (!groups.length) {
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'No series / courses found. Run a scan or use 🤖 Auto-Classify.';
    grid.appendChild(msg);
    return;
  }

  groups.forEach(group => {
    const card = document.createElement('div');
    card.className = 'card group-card';
    card.title = `${group.count} episode${group.count !== 1 ? 's' : ''}`;

    card.addEventListener('click', () => {
      state.drillGroup = { name: group.name, type: group.type };
      renderBreadcrumb(group.name);
      loadMedia();
    });

    const img = document.createElement('img');
    img.src     = group.posterPath || '/img/no-poster.svg';
    img.alt     = group.name;
    img.loading = 'lazy';
    img.onerror = () => { img.onerror = null; img.src = '/img/no-poster.svg'; };

    const info = document.createElement('div');
    info.className = 'info';

    const titleEl = document.createElement('div');
    titleEl.className = 'card-title';
    titleEl.textContent = group.name;

    const metaEl = document.createElement('div');
    metaEl.className = 'meta';
    metaEl.textContent = `${group.count} ${state.type === 'Education' ? 'lesson' : 'episode'}${group.count !== 1 ? 's' : ''}` +
                         (group.year ? ` · ${group.year}` : '');

    info.appendChild(titleEl);
    info.appendChild(metaEl);
    card.appendChild(img);
    card.appendChild(info);
    grid.appendChild(card);
  });
}

// ── Series drill-down — list of episodes/lessons ──────────────────────────────

async function loadSeriesEpisodes(seriesName) {
  const grid = document.getElementById('grid');
  grid.textContent = '';

  try {
    const items = await API.get(`/api/media/series/${encodeURIComponent(seriesName)}`);
    state.total = items.length;
    if (state.viewMode === 'list') renderTable(items);
    else renderEpisodeCards(items);
    document.getElementById('pagination').textContent = '';
  } catch {
    grid.textContent = '';
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'Failed to load episodes.';
    grid.appendChild(msg);
  }
}

// Episode/lesson cards — show S##E## or Lesson ## badge, plus play button
function renderEpisodeCards(items) {
  const grid = document.getElementById('grid');
  grid.textContent = '';
  grid.classList.remove('list-mode');

  if (!items.length) {
    const msg = document.createElement('div');
    msg.className = 'spinner';
    msg.textContent = 'No episodes found.';
    grid.appendChild(msg);
    return;
  }

  items.forEach(item => {
    const card = document.createElement('div');
    card.className = 'card episode-card' + (selectedIds.has(item.id) ? ' card-selected' : '');
    card.dataset.id = item.id;

    card.addEventListener('click', () => {
      if (selectMode) { toggleSelect(item.id, card); return; }
      openPlayer(item.id);
    });

    const img = document.createElement('img');
    img.src     = item.posterPath || '/img/no-poster.svg';
    img.alt     = item.title;
    img.loading = 'lazy';
    img.onerror = () => { img.onerror = null; img.src = '/img/no-poster.svg'; };

    // Episode badge
    const badge = document.createElement('div');
    badge.className = 'ep-badge';
    if (item.seasonNumber && item.episodeNumber) {
      badge.textContent = `S${String(item.seasonNumber).padStart(2,'0')}E${String(item.episodeNumber).padStart(2,'0')}`;
    } else if (item.episodeNumber) {
      const label = item.type === 'Education' ? 'Lesson' : 'Ep';
      badge.textContent = `${label} ${String(item.episodeNumber).padStart(2,'0')}`;
    }

    // Select / fav / edit overlays
    const chk = document.createElement('div');
    chk.className = 'card-check' + (selectedIds.has(item.id) ? ' checked' : '');
    chk.textContent = selectedIds.has(item.id) ? '✓' : '';

    const favBtn = document.createElement('button');
    favBtn.className = 'card-fav' + (item.isFavorite ? ' fav-active' : '');
    favBtn.textContent = item.isFavorite ? '♥' : '♡';
    favBtn.addEventListener('click', e => { e.stopPropagation(); toggleFav(item.id); });

    const editBtn = document.createElement('button');
    editBtn.className = 'card-edit';
    editBtn.textContent = '✏';
    editBtn.addEventListener('click', e => { e.stopPropagation(); openEditModal(item); });

    const info = document.createElement('div');
    info.className = 'info';

    const titleEl = document.createElement('div');
    titleEl.className = 'card-title';
    titleEl.textContent = item.title;

    const metaEl = document.createElement('div');
    metaEl.className = 'meta';
    const metaParts = [];
    if (item.seasonNumber) metaParts.push(`Season ${item.seasonNumber}`);
    if (item.year) metaParts.push(item.year);
    if (item.rating) metaParts.push(`★ ${item.rating.toFixed(1)}`);
    metaEl.textContent = metaParts.join(' · ');

    info.appendChild(titleEl);
    info.appendChild(metaEl);

    card.appendChild(img);
    if (badge.textContent) card.appendChild(badge);
    card.appendChild(chk);
    card.appendChild(favBtn);
    card.appendChild(editBtn);
    card.appendChild(info);
    grid.appendChild(card);
  });
}

// ── Breadcrumb for drill-down ─────────────────────────────────────────────────

function renderBreadcrumb(groupName) {
  const existing = document.getElementById('breadcrumb');
  if (existing) existing.remove();

  const bc = document.createElement('div');
  bc.id = 'breadcrumb';
  bc.className = 'breadcrumb';

  const back = document.createElement('button');
  back.className = 'filter-btn breadcrumb-back';
  back.textContent = `← ${state.type === 'Education' ? 'Courses' : 'Series'}`;
  back.addEventListener('click', () => {
    state.drillGroup = null;
    bc.remove();
    loadMedia();
  });

  const sep = document.createElement('span');
  sep.textContent = ' / ';
  sep.className = 'bc-sep';

  const current = document.createElement('span');
  current.className = 'bc-current';
  current.textContent = groupName;

  bc.appendChild(back);
  bc.appendChild(sep);
  bc.appendChild(current);

  // Insert after filters bar
  const filters = document.querySelector('.filters');
  filters.insertAdjacentElement('afterend', bc);
}

// ── Rendering — dispatches ────────────────────────────────────────────────────

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
    card.className = 'card' + (selectedIds.has(item.id) ? ' card-selected' : '');
    card.setAttribute('role', 'listitem');
    card.dataset.id = item.id;

    card.addEventListener('click', () => {
      if (selectMode) { toggleSelect(item.id, card); return; }
      openPlayer(item.id);
    });

    const img = document.createElement('img');
    img.src = item.posterPath || '/img/no-poster.svg';
    img.alt = item.title;
    img.loading = 'lazy';
    img.onerror = () => { img.onerror = null; img.src = '/img/no-poster.svg'; };

    const chk = document.createElement('div');
    chk.className = 'card-check' + (selectedIds.has(item.id) ? ' checked' : '');
    chk.textContent = selectedIds.has(item.id) ? '✓' : '';

    const favBtn = document.createElement('button');
    favBtn.className = 'card-fav' + (item.isFavorite ? ' fav-active' : '');
    favBtn.textContent = item.isFavorite ? '♥' : '♡';
    favBtn.title = item.isFavorite ? 'Remove from favorites' : 'Add to favorites';
    favBtn.addEventListener('click', e => { e.stopPropagation(); toggleFav(item.id); });

    const editBtn = document.createElement('button');
    editBtn.className = 'card-edit';
    editBtn.textContent = '✏';
    editBtn.title = 'Edit / Classify';
    editBtn.addEventListener('click', e => { e.stopPropagation(); openEditModal(item); });

    const info = document.createElement('div');
    info.className = 'info';

    const titleEl = document.createElement('div');
    titleEl.className = 'card-title';
    titleEl.textContent = item.title;

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
    card.appendChild(chk);
    card.appendChild(favBtn);
    card.appendChild(editBtn);
    card.appendChild(info);
    grid.appendChild(card);
  });
}

// List (table) view
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

  const headerRow = table.createTHead().insertRow();
  const thChk = document.createElement('th');
  thChk.style.width = '36px';
  if (selectMode) {
    const allChk = document.createElement('input');
    allChk.type = 'checkbox';
    allChk.title = 'Select all on page';
    allChk.addEventListener('change', e => {
      items.forEach(i => { if (e.target.checked) selectedIds.add(i.id); else selectedIds.delete(i.id); });
      updateBulkBar();
      renderTable(items);
    });
    thChk.appendChild(allChk);
  }
  headerRow.appendChild(thChk);

  // Show episode column when drilling into a series
  const cols = state.drillGroup
    ? ['Title', 'Ep', 'Season', 'Rating', 'Actions']
    : ['Title', 'Section', 'Year', 'Genres', 'Rating', 'Actions'];
  cols.forEach(col => {
    const th = document.createElement('th');
    th.textContent = col;
    headerRow.appendChild(th);
  });

  const tbody = table.createTBody();
  items.forEach(item => {
    const row = tbody.insertRow();
    if (selectedIds.has(item.id)) row.classList.add('row-selected');

    const chkCell = row.insertCell();
    if (selectMode) {
      const chk = document.createElement('input');
      chk.type = 'checkbox';
      chk.checked = selectedIds.has(item.id);
      chk.addEventListener('change', e => {
        if (e.target.checked) selectedIds.add(item.id); else selectedIds.delete(item.id);
        row.classList.toggle('row-selected', e.target.checked);
        updateBulkBar();
      });
      chkCell.appendChild(chk);
    }

    const titleCell = row.insertCell();
    titleCell.className = 'cell-title';
    titleCell.textContent = item.title;
    titleCell.title = item.title;

    if (state.drillGroup) {
      const epCell = row.insertCell();
      if (item.seasonNumber && item.episodeNumber)
        epCell.textContent = `S${String(item.seasonNumber).padStart(2,'0')}E${String(item.episodeNumber).padStart(2,'0')}`;
      else if (item.episodeNumber)
        epCell.textContent = `#${item.episodeNumber}`;
      else epCell.textContent = '—';

      const seasonCell = row.insertCell();
      seasonCell.textContent = item.seasonNumber ?? '—';
    } else {
      const typeCell = row.insertCell();
      typeCell.textContent = item.type || '—';
      const yearCell = row.insertCell();
      yearCell.textContent = item.year || '—';
      const genresCell = row.insertCell();
      genresCell.className = 'cell-genres';
      genresCell.textContent = item.genres || '—';
      genresCell.title = item.genres || '';
    }

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
    favBtn.addEventListener('click', () => toggleFav(item.id));

    const delBtn = document.createElement('button');
    delBtn.className = 'filter-btn btn-danger';
    delBtn.textContent = '🗑';
    delBtn.title = 'Remove from library';
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

// ── Auto-classify ─────────────────────────────────────────────────────────────

async function autoClassify() {
  const btn = document.getElementById('classifyBtn');
  btn.disabled = true;
  btn.textContent = '⏳ Classifying…';
  try {
    const res = await API.post('/api/media/auto-classify', {});
    showToast(
      `✓ Auto-classify done — ${res.reclassified} reclassified, ${res.skipped} unchanged.`,
      6000
    );
    // Reset drill-down and reload
    state.drillGroup = null;
    const bc = document.getElementById('breadcrumb');
    if (bc) bc.remove();
    loadMedia();
  } catch {
    showToast('Auto-classify failed.');
  } finally {
    btn.disabled = false;
    btn.textContent = '🤖 Auto-Classify';
  }
}

// ── Edit modal ────────────────────────────────────────────────────────────────

function openEditModal(item) {
  editingId = item.id;
  document.getElementById('eTitle').value   = item.title        || '';
  document.getElementById('eType').value    = item.type         || 'Movie';
  document.getElementById('eYear').value    = item.year         ?? '';
  document.getElementById('eRating').value  = item.rating       ?? '';
  document.getElementById('eGenres').value  = item.genres       || '';
  document.getElementById('eDesc').value    = item.description  || '';
  const s = document.getElementById('tmdbStatus');
  s.textContent = '';
  s.className = 'tmdb-status';
  document.getElementById('tmdbRefreshBtn').disabled = false;
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
  } catch { showToast('Delete failed.'); }
}

async function toggleFav(id) {
  try { await API.post(`/api/media/${id}/favorite`, {}); loadMedia(); }
  catch { showToast('Failed to update favorite.'); }
}

document.getElementById('editModal').addEventListener('click', e => {
  if (e.target === e.currentTarget) e.currentTarget.close();
});

// ── TMDB refresh ──────────────────────────────────────────────────────────────

async function refreshTmdb() {
  if (!editingId) return;
  const btn    = document.getElementById('tmdbRefreshBtn');
  const status = document.getElementById('tmdbStatus');
  btn.disabled = true;
  status.textContent = '⏳ Fetching from TMDB…';
  status.className = 'tmdb-status tmdb-pending';

  try {
    const res = await API.post(`/api/media/${editingId}/refresh-tmdb`, {});
    if (res.success) {
      status.textContent = '✓ ' + res.message;
      status.className = 'tmdb-status tmdb-ok';
      if (res.item) {
        if (res.item.description) document.getElementById('eDesc').value   = res.item.description;
        if (res.item.genres)      document.getElementById('eGenres').value = res.item.genres;
        if (res.item.rating)      document.getElementById('eRating').value = res.item.rating;
      }
      loadMedia();
    } else {
      status.textContent = '✗ ' + res.message;
      status.className = 'tmdb-status tmdb-fail';
      btn.disabled = false;
    }
  } catch (e) {
    status.textContent = '✗ Request failed: ' + e.message;
    status.className = 'tmdb-status tmdb-fail';
    btn.disabled = false;
  }
}

// ── Add Media modal ───────────────────────────────────────────────────────────

function openAddModal() {
  document.getElementById('aFilePath').value = '';
  document.getElementById('aTitle').value    = '';
  document.getElementById('aType').value     = 'Movie';
  document.getElementById('aYear').value     = '';
  document.getElementById('addModal').showModal();
}

function closeAddModal() { document.getElementById('addModal').close(); }

function autofillTitle() {
  const fp = document.getElementById('aFilePath').value.trim();
  const filename = fp.split(/[\\/]/).pop() || '';
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
    showToast(e.message.includes('409') ? 'Already in library.' : 'Add failed.');
  }
}

document.getElementById('addModal').addEventListener('click', e => {
  if (e.target === e.currentTarget) e.currentTarget.close();
});

// ── Player ────────────────────────────────────────────────────────────────────

function openPlayer(id) { location.href = `/player.html?id=${id}`; }

// ── Pagination ────────────────────────────────────────────────────────────────

function renderPagination() {
  const totalPages = Math.max(1, Math.ceil(state.total / state.limit));
  const el = document.getElementById('pagination');
  el.textContent = '';
  if (totalPages <= 1) return;

  const prev = document.createElement('button');
  prev.textContent = '← Prev';
  prev.disabled = state.page <= 1;
  prev.addEventListener('click', () => { state.page--; loadMedia(); });
  el.appendChild(prev);

  const info = document.createElement('span');
  info.textContent = `Page ${state.page} / ${totalPages}`;
  info.style.cssText = 'padding:0 12px;color:var(--text2);font-size:.85rem;line-height:32px';
  el.appendChild(info);

  const next = document.createElement('button');
  next.textContent = 'Next →';
  next.disabled = state.page >= totalPages;
  next.addEventListener('click', () => { state.page++; loadMedia(); });
  el.appendChild(next);
}

// ── Selection / bulk ──────────────────────────────────────────────────────────

function toggleSelectMode() {
  selectMode = !selectMode;
  selectedIds.clear();
  const btn = document.getElementById('selectBtn');
  btn.textContent = selectMode ? '✕ Cancel Select' : '☑ Select';
  btn.classList.toggle('active', selectMode);
  document.getElementById('bulkBar').style.display = selectMode ? 'flex' : 'none';
  document.body.classList.toggle('select-mode', selectMode);
  updateBulkBar();
  loadMedia();
}

function toggleSelect(id, cardEl) {
  if (selectedIds.has(id)) { selectedIds.delete(id); cardEl.classList.remove('card-selected'); }
  else                     { selectedIds.add(id);    cardEl.classList.add('card-selected'); }
  updateBulkBar();
}

function updateBulkBar() {
  document.getElementById('bulkCount').textContent = `${selectedIds.size} selected`;
}

function clearSelection() {
  selectedIds.clear();
  updateBulkBar();
  loadMedia();
}

async function applyBulkType() {
  if (selectedIds.size === 0) { showToast('No items selected.'); return; }
  const type = document.getElementById('bulkType').value;
  const ids = [...selectedIds];
  try {
    const res = await API.post('/api/media/bulk-type', { ids, type });
    showToast(`✓ Moved ${res.updated} item(s) to ${type}.`, 4000);
    selectedIds.clear();
    updateBulkBar();
    loadMedia();
  } catch { showToast('Bulk update failed.'); }
}

// ── Filter / view controls ────────────────────────────────────────────────────

function syncAllBtn() {
  document.getElementById('allBtn').classList.toggle('active', !state.type && !state.favorites);
  document.querySelectorAll('.filter-btn[data-type]').forEach(b =>
    b.classList.toggle('active', b.dataset.type === state.type));
}

function clearFilters() {
  state.type = null; state.page = 1; state.favorites = false;
  state.drillGroup = null;
  const bc = document.getElementById('breadcrumb');
  if (bc) bc.remove();
  savePrefs();
  document.getElementById('favBtn').classList.remove('active');
  loadMedia();
}

function setFilter(type) {
  // Reset drill-down when changing section
  if (state.type !== type) {
    state.drillGroup = null;
    const bc = document.getElementById('breadcrumb');
    if (bc) bc.remove();
  }
  state.type = state.type === type ? null : type;
  state.page = 1;
  savePrefs();
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
  savePrefs();
  loadMedia();
}

document.getElementById('searchInput').addEventListener('input', e => {
  state.search = e.target.value;
  state.page = 1;
  // Search escapes group view — show flat results
  if (state.search) {
    state.drillGroup = null;
    const bc = document.getElementById('breadcrumb');
    if (bc) bc.remove();
  }
  clearTimeout(window._searchTimer);
  window._searchTimer = setTimeout(loadMedia, 400);
});

// ── Sort ──────────────────────────────────────────────────────────────────────

function setSort(field) {
  if (state.sortBy === field) {
    state.sortDesc = !state.sortDesc;
  } else {
    state.sortBy = field;
    state.sortDesc = field === 'dateAdded';
  }
  state.page = 1;
  savePrefs();
  syncSortButtons();
  loadMedia();
}

function syncSortButtons() {
  ['dateAdded', 'title', 'year', 'rating'].forEach(f => {
    const btn = document.getElementById('sort_' + f);
    if (!btn) return;
    const active = state.sortBy === f;
    btn.classList.toggle('active', active);
    const arrow = active ? (state.sortDesc ? ' ↓' : ' ↑') : '';
    btn.dataset.label = btn.dataset.label || btn.textContent.replace(/ [↑↓]$/, '');
    btn.textContent = btn.dataset.label + arrow;
  });
}

// ── Scan ──────────────────────────────────────────────────────────────────────

let _hubConnection = null;

async function connectScanHub() {
  if (_hubConnection) return;
  if (typeof signalR === 'undefined') return;
  _hubConnection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/scan')
    .withAutomaticReconnect()
    .build();

  _hubConnection.on('ScanStarted', data => {
    showToast('Scan started — ' + (data.folders || []).length + ' folder(s)…', 8000);
  });
  _hubConnection.on('ScanProgress', data => {
    const btn = document.getElementById('scanBtn');
    if (btn) btn.textContent = `Scanning… (${data.current}/${data.total})`;
  });
  _hubConnection.on('ScanCompleted', data => {
    const btn = document.getElementById('scanBtn');
    if (btn) { btn.textContent = 'Scan Library'; btn.disabled = false; }
    const errors = (data.errors || []).length;
    const msg = `Scan done ✓  Added: ${data.added}  Skipped: ${data.skipped}` +
      (errors > 0 ? `  Errors: ${errors}` : '');
    showToast(msg, 6000);
    loadMedia();
  });
  _hubConnection.on('ScanError', data => {
    const btn = document.getElementById('scanBtn');
    if (btn) { btn.textContent = 'Scan Library'; btn.disabled = false; }
    showToast('Scan failed: ' + (data.error || 'Unknown error'), 8000);
  });

  try { await _hubConnection.start(); } catch { /* hub optional */ }
}

async function triggerScan() {
  const btn = document.getElementById('scanBtn');
  btn.textContent = 'Scanning…';
  btn.disabled = true;
  try {
    await API.post('/api/media/scan', {});
  } catch (e) {
    showToast('Scan request failed: ' + e.message);
    btn.textContent = 'Scan Library';
    btn.disabled = false;
  }
}

// ── Toast ─────────────────────────────────────────────────────────────────────

function showToast(msg, ms = 3000) {
  const t = document.getElementById('toast');
  t.textContent = msg;
  t.style.opacity = '1';
  clearTimeout(window._toastTimer);
  window._toastTimer = setTimeout(() => { t.style.opacity = '0'; }, ms);
}

// ── Init ──────────────────────────────────────────────────────────────────────

connectScanHub();
loadMedia();
