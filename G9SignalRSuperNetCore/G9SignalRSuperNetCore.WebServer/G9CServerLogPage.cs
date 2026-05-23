namespace G9SignalRSuperNetCore.WebServer;

/// <summary>The static HTML payload served at <c>/logs</c>.</summary>
internal static class G9CServerLogPage
{
    /// <summary>The HTML+JS document. Self-contained; loads SignalR from the official CDN.</summary>
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<title>G9SignalRSuperNetCore — server logs</title>
<style>
  :root { color-scheme: dark; }
  body { margin: 0; font-family: ui-monospace, Menlo, Consolas, monospace; background: #0e1117; color: #c9d1d9; }
  header { padding: 8px 14px; background: #161b22; border-bottom: 1px solid #30363d; display: flex; gap: 12px; align-items: center; }
  header h1 { font-size: 14px; font-weight: 600; margin: 0; color: #58a6ff; flex: 1; }
  header label { font-size: 12px; color: #8b949e; }
  header input[type=text] { background: #0d1117; color: #c9d1d9; border: 1px solid #30363d; padding: 3px 6px; font: inherit; min-width: 220px; }
  header button { background: #21262d; color: #c9d1d9; border: 1px solid #30363d; padding: 3px 10px; font: inherit; cursor: pointer; }
  header button:hover { background: #30363d; }
  #status { font-size: 12px; padding: 2px 8px; border-radius: 12px; background: #21262d; }
  #status.ok { color: #3fb950; }
  #status.bad { color: #f85149; }
  main { padding: 0; }
  .line { display: grid; grid-template-columns: 110px 50px 260px 1fr; gap: 8px; padding: 2px 14px; border-bottom: 1px solid #161b22; font-size: 12px; line-height: 1.4; }
  .line:hover { background: #161b22; }
  .ts { color: #6e7681; }
  .lvl { font-weight: 700; text-transform: uppercase; }
  .lvl.trace { color: #6e7681; }
  .lvl.debug { color: #8b949e; }
  .lvl.info  { color: #58a6ff; }
  .lvl.warn  { color: #d29922; }
  .lvl.error { color: #f85149; }
  .lvl.crit  { color: #f85149; background: #3b0d0d; padding: 0 6px; }
  .cat { color: #d2a8ff; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .msg { color: #c9d1d9; white-space: pre-wrap; word-break: break-word; }
  .exn { color: #f85149; font-style: italic; }
</style>
</head>
<body>
<header>
  <h1>G9SignalRSuperNetCore — live server log</h1>
  <span id="status" class="bad">connecting…</span>
  <label>filter: <input id="filter" type="text" placeholder="text or category" /></label>
  <label>level ≥
    <select id="minLevel">
      <option value="0">trace</option>
      <option value="1">debug</option>
      <option value="2" selected>info</option>
      <option value="3">warn</option>
      <option value="4">error</option>
    </select>
  </label>
  <button id="clear">clear</button>
  <label><input id="autoscroll" type="checkbox" checked /> autoscroll</label>
</header>
<main id="log"></main>

<script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.7/signalr.min.js"></script>
<script>
  const levelRank = { trace: 0, debug: 1, info: 2, warn: 3, error: 4, crit: 5 };
  const log = document.getElementById('log');
  const status = document.getElementById('status');
  const filter = document.getElementById('filter');
  const minLevel = document.getElementById('minLevel');
  const autoscroll = document.getElementById('autoscroll');
  document.getElementById('clear').onclick = () => { log.innerHTML = ''; };

  function visible(entry) {
    if ((levelRank[entry.level] ?? 2) < parseInt(minLevel.value, 10)) return false;
    const term = filter.value.trim().toLowerCase();
    if (!term) return true;
    return entry.category.toLowerCase().includes(term)
        || (entry.message && entry.message.toLowerCase().includes(term));
  }

  function render(entry) {
    if (!visible(entry)) return;
    const div = document.createElement('div');
    div.className = 'line';
    const ts = new Date(entry.utcTimestamp);
    const tsText = ts.toISOString().substr(11, 12);
    div.innerHTML = `
      <span class="ts">${tsText}</span>
      <span class="lvl ${entry.level}">${entry.level}</span>
      <span class="cat" title="${entry.category}">${entry.category}</span>
      <span class="msg">${escapeHtml(entry.message)}${entry.exception ? `\n<span class="exn">${escapeHtml(entry.exception)}</span>` : ''}</span>
    `;
    log.appendChild(div);
    while (log.childElementCount > 1500) log.removeChild(log.firstChild);
    if (autoscroll.checked) window.scrollTo(0, document.body.scrollHeight);
  }

  function escapeHtml(s) {
    if (s == null) return '';
    return String(s).replace(/[&<>"']/g, c =>
      ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
  }

  filter.addEventListener('input', () => { /* live; new entries respect it. */ });
  minLevel.addEventListener('change', () => { /* live; new entries respect it. */ });

  const conn = new signalR.HubConnectionBuilder()
    .withUrl('/__g9logs')
    .withAutomaticReconnect()
    .build();

  conn.onreconnecting(() => { status.textContent = 'reconnecting…'; status.className = 'bad'; });
  conn.onreconnected(() => { status.textContent = 'connected'; status.className = 'ok'; subscribe(); });
  conn.onclose(() => { status.textContent = 'disconnected'; status.className = 'bad'; });

  async function start() {
    try {
      await conn.start();
      status.textContent = 'connected';
      status.className = 'ok';
      subscribe();
    } catch (e) {
      status.textContent = 'error: ' + e;
      setTimeout(start, 2000);
    }
  }

  function subscribe() {
    conn.stream('Subscribe').subscribe({
      next: render,
      error: e => { status.textContent = 'stream err'; status.className = 'bad'; },
      complete: () => { /* server closed */ }
    });
  }

  start();
</script>
</body>
</html>
""";
}
