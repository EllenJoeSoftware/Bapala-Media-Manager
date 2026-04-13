const id = new URLSearchParams(location.search).get('id');
if (!id) location.href = '/index.html';

const vid = document.getElementById('vid');
let saveTimer = null;
let lastSavedPosition = 0;

// ── Save position to server ──────────────────────────────────────────────────

function savePosition(pos) {
  const rounded = Math.floor(pos);
  if (rounded === lastSavedPosition || rounded < 2) return;
  lastSavedPosition = rounded;
  API.post(`/api/media/${id}/progress`, { progressSeconds: rounded }).catch(() => {});
}

// Save immediately (no debounce) — used on pause / beforeunload / ended
function saveNow() {
  clearTimeout(saveTimer);
  saveTimer = null;
  if (vid.currentTime > 2) savePosition(vid.currentTime);
}

// ── Resume banner ────────────────────────────────────────────────────────────

function showResumeBanner(seconds) {
  const banner = document.getElementById('resumeBanner');
  const timeStr = formatTime(seconds);
  document.getElementById('resumeTime').textContent = timeStr;
  banner.style.display = 'flex';

  document.getElementById('resumeYes').onclick = () => {
    vid.currentTime = seconds;
    vid.play();
    banner.style.display = 'none';
  };
  document.getElementById('resumeNo').onclick = () => {
    vid.currentTime = 0;
    vid.play();
    banner.style.display = 'none';
  };
}

function formatTime(s) {
  s = Math.floor(s);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (h > 0) return `${h}:${String(m).padStart(2,'0')}:${String(sec).padStart(2,'0')}`;
  return `${m}:${String(sec).padStart(2,'0')}`;
}

// ── Init ─────────────────────────────────────────────────────────────────────

async function init() {
  try {
    const [item, progress] = await Promise.all([
      API.get(`/api/media/${id}`),
      API.get(`/api/media/${id}/progress`)
    ]);

    document.title = `${item.title} — Bapala`;
    document.getElementById('title').textContent = item.title;
    document.getElementById('meta').textContent =
      [item.year, item.genres, item.rating != null ? `${item.rating.toFixed(1)} / 10` : null]
        .filter(Boolean).join(' · ');
    document.getElementById('desc').textContent = item.description || '';

    vid.src = API.streamUrl(id);
    lastSavedPosition = progress.progressSeconds || 0;

    vid.addEventListener('loadedmetadata', () => {
      const saved = progress.progressSeconds || 0;
      // Only offer resume if we're more than 30s in and not within 30s of the end
      const nearEnd = vid.duration > 0 && saved >= vid.duration - 30;
      if (saved > 30 && !nearEnd) {
        showResumeBanner(saved);
      }
    }, { once: true });

    // Debounced periodic save (every 10s of playback)
    vid.addEventListener('timeupdate', () => {
      clearTimeout(saveTimer);
      saveTimer = setTimeout(() => savePosition(vid.currentTime), 10000);
    });

    // Immediate save on pause, end, or leaving the page
    vid.addEventListener('pause',  saveNow);
    vid.addEventListener('ended',  () => {
      // Mark as finished — save 0 so next play starts from beginning
      lastSavedPosition = 0;
      API.post(`/api/media/${id}/progress`, { progressSeconds: 0 }).catch(() => {});
    });

    window.addEventListener('beforeunload', saveNow);

    // Also save on visibility change (tab switch / minimise on mobile)
    document.addEventListener('visibilitychange', () => {
      if (document.hidden) saveNow();
    });

  } catch {
    document.getElementById('title').textContent = 'Error loading media.';
  }
}

init();
