const id = new URLSearchParams(location.search).get('id');
if (!id) location.href = '/index.html';

const vid = document.getElementById('vid');
let saveTimer;

async function init() {
  try {
    const [item, progress] = await Promise.all([
      API.get(`/api/media/${id}`),
      API.get(`/api/media/${id}/progress`)
    ]);

    document.title = `${item.title} — Bapala`;

    // Safe DOM text — no innerHTML with server data
    document.getElementById('title').textContent = item.title;
    document.getElementById('meta').textContent =
      [item.year, item.genres, item.rating != null ? `${item.rating.toFixed(1)} / 10` : null]
        .filter(Boolean).join(' · ');
    document.getElementById('desc').textContent = item.description || '';

    vid.src = API.streamUrl(id);

    if (progress.progressSeconds > 30) {
      vid.addEventListener('loadedmetadata', () => {
        vid.currentTime = progress.progressSeconds;
      }, { once: true });
    }

    vid.addEventListener('timeupdate', () => {
      clearTimeout(saveTimer);
      saveTimer = setTimeout(() => {
        API.post(`/api/media/${id}/progress`, { progressSeconds: Math.floor(vid.currentTime) })
          .catch(() => {});
      }, 10000);
    });
  } catch {
    document.getElementById('title').textContent = 'Error loading media.';
  }
}

init();
