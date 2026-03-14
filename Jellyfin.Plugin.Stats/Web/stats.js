(function () {
  'use strict';

  // ── Constants ──────────────────────────────────────────────────────────────
  const PAGE_ID = 'jf-stats-page';
  const BTN_ID  = 'jf-stats-btn';

  // ── Auth ───────────────────────────────────────────────────────────────────
  function getAuth() {
    try {
      const creds = JSON.parse(localStorage.getItem('jellyfin_credentials') || '{}');
      const s = creds?.Servers?.[0];
      if (s?.AccessToken && s?.UserId) return {
        token: s.AccessToken,
        userId: s.UserId,
        serverUrl: (s.ManualAddress || s.LocalAddress || location.origin).replace(/\/$/, ''),
      };
    } catch {}
    return null;
  }

  const HEADERS = auth => ({
    Authorization: `MediaBrowser Client="JellyStats",Device="Browser",DeviceId="jfstats1",Version="1.0",Token="${auth.token}"`,
  });

  async function jellyApi(path, auth) {
    const r = await fetch(`${auth.serverUrl}${path}`, { headers: HEADERS(auth) });
    if (!r.ok) throw new Error(`${r.status} ${path.slice(0, 50)}`);
    return r.json();
  }

  async function statsApi(path, auth) {
    const r = await fetch(`${auth.serverUrl}${path}`, { headers: HEADERS(auth) });
    if (!r.ok) throw new Error(`${r.status} ${path.slice(0, 50)}`);
    return r.json();
  }

  function timeAgo(d) {
    if (!d) return '';
    const days = Math.floor((Date.now() - new Date(d)) / 86400000);
    if (days === 0) return 'Today';
    if (days === 1) return 'Yesterday';
    if (days < 7)  return `${days}d ago`;
    if (days < 30) return `${Math.floor(days / 7)}w ago`;
    return `${Math.floor(days / 30)}mo ago`;
  }

  function fmtHours(ticks) {
    const hrs = Math.round(ticks / 36_000_000_000);
    return hrs >= 24
      ? `${hrs}<span>hrs</span> <small style="font-size:.7rem;color:rgba(255,255,255,.35)">${Math.round(hrs/24)}d</small>`
      : `${hrs}<span>hrs</span>`;
  }

  // ── Styles ─────────────────────────────────────────────────────────────────
  const style = document.createElement('style');
  style.textContent = `
    @import url('https://fonts.googleapis.com/css2?family=Rajdhani:wght@400;500;600;700&display=swap');
    #${PAGE_ID} {
      position:fixed;inset:0;z-index:99998;background:#0a0a0a;
      font-family:'Rajdhani',sans-serif;color:#fff;overflow-y:auto;
      display:none;flex-direction:column;
    }
    #${PAGE_ID}.open { display:flex; }
    #${PAGE_ID}::before {
      content:'';position:fixed;inset:0;pointer-events:none;z-index:0;
      background-image:
        repeating-linear-gradient(to bottom,rgba(255,255,255,.022) 0,rgba(255,255,255,.022) 1px,transparent 1px,transparent 40px),
        repeating-linear-gradient(to right,rgba(255,255,255,.014) 0,rgba(255,255,255,.014) 1px,transparent 1px,transparent 80px);
      mask-image:radial-gradient(ellipse 90% 90% at 50% 50%,rgba(0,0,0,.6) 0%,rgba(0,0,0,.3) 50%,transparent 80%);
    }
    .jfs-inner{position:relative;z-index:1;max-width:1100px;margin:0 auto;padding:32px 24px 60px;width:100%;box-sizing:border-box}
    .jfs-header{display:flex;align-items:center;justify-content:space-between;margin-bottom:28px;border-bottom:1px solid rgba(255,255,255,.07);padding-bottom:20px}
    .jfs-title{font-size:2rem;font-weight:700;letter-spacing:.3em;text-transform:uppercase}
    .jfs-title span{color:#e50914}
    .jfs-close{background:transparent;border:1px solid rgba(255,255,255,.15);color:#fff;font-family:'Rajdhani',sans-serif;font-size:.95rem;letter-spacing:.1em;padding:8px 20px;border-radius:4px;cursor:pointer;transition:border-color .2s,background .2s}
    .jfs-close:hover{border-color:#e50914;background:rgba(229,9,20,.08)}
    .jfs-server-banner{display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:12px;margin-bottom:28px}
    .jfs-server-card{background:rgba(229,9,20,.07);border:1px solid rgba(229,9,20,.2);border-radius:8px;padding:14px 16px}
    .jfs-server-card-label{font-size:.72rem;letter-spacing:.15em;text-transform:uppercase;color:rgba(255,255,255,.4);margin-bottom:6px}
    .jfs-server-card-value{font-size:1.6rem;font-weight:700;line-height:1}
    .jfs-server-card-value span{font-size:.8rem;color:rgba(255,255,255,.4);font-weight:400;margin-left:3px}
    .jfs-tabs{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:28px}
    .jfs-tab{background:rgba(255,255,255,.05);border:1px solid rgba(255,255,255,.1);color:rgba(255,255,255,.6);font-family:'Rajdhani',sans-serif;font-size:.9rem;letter-spacing:.08em;padding:6px 16px;border-radius:4px;cursor:pointer;transition:all .2s}
    .jfs-tab.active,.jfs-tab:hover{border-color:#e50914;color:#fff;background:rgba(229,9,20,.1)}
    .jfs-cards{display:grid;grid-template-columns:repeat(auto-fit,minmax(155px,1fr));gap:14px;margin-bottom:28px}
    .jfs-card{background:rgba(255,255,255,.04);border:1px solid rgba(255,255,255,.08);border-radius:8px;padding:18px 16px;transition:border-color .2s}
    .jfs-card:hover{border-color:rgba(229,9,20,.4)}
    .jfs-card-label{font-size:.72rem;letter-spacing:.15em;text-transform:uppercase;color:rgba(255,255,255,.4);margin-bottom:6px}
    .jfs-card-value{font-size:1.9rem;font-weight:700;line-height:1}
    .jfs-card-value span{font-size:.85rem;color:rgba(255,255,255,.4);font-weight:400;margin-left:3px}
    .jfs-card-sub{font-size:.75rem;color:#e50914;margin-top:5px;letter-spacing:.05em}
    .jfs-2col{display:grid;grid-template-columns:1fr 1fr;gap:18px;margin-bottom:18px}
    .jfs-3col{display:grid;grid-template-columns:1fr 1fr 1fr;gap:18px;margin-bottom:18px}
    @media(max-width:800px){.jfs-2col,.jfs-3col{grid-template-columns:1fr}}
    .jfs-section{background:rgba(255,255,255,.03);border:1px solid rgba(255,255,255,.07);border-radius:8px;padding:18px;margin-bottom:18px}
    .jfs-section-title{font-size:.75rem;letter-spacing:.2em;text-transform:uppercase;color:rgba(255,255,255,.4);margin-bottom:14px;display:flex;align-items:center;gap:8px}
    .jfs-section-title::after{content:'';flex:1;height:1px;background:rgba(255,255,255,.07)}
    .jfs-chart-controls{display:flex;gap:6px;margin-bottom:10px}
    .jfs-chart-btn{font-size:.72rem;padding:3px 10px;border-radius:3px;border:1px solid rgba(255,255,255,.1);color:rgba(255,255,255,.4);background:transparent;cursor:pointer;font-family:'Rajdhani',sans-serif;letter-spacing:.08em;transition:all .2s}
    .jfs-chart-btn.active{border-color:#e50914;color:#fff;background:rgba(229,9,20,.12)}
    .jfs-chart-wrap{display:flex;align-items:flex-end;gap:3px;height:80px;margin-top:4px}
    .jfs-chart-col{display:flex;flex-direction:column;align-items:center;flex:1;height:100%;justify-content:flex-end;gap:3px}
    .jfs-chart-bar{width:100%;background:linear-gradient(180deg,#e50914,#a00008);border-radius:3px 3px 0 0;transition:height .8s cubic-bezier(.2,.8,.3,1);min-height:2px}
    .jfs-chart-lbl{font-size:.52rem;color:rgba(255,255,255,.25);text-align:center;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:100%}
    .jfs-genre-row{display:flex;align-items:center;gap:10px;margin-bottom:9px}
    .jfs-genre-name{font-size:.85rem;width:95px;flex-shrink:0;color:rgba(255,255,255,.8)}
    .jfs-genre-bar-wrap{flex:1;height:4px;background:rgba(255,255,255,.08);border-radius:4px;overflow:hidden}
    .jfs-genre-bar{height:100%;border-radius:4px;background:linear-gradient(90deg,#a00008,#e50914);transition:width .9s cubic-bezier(.2,.8,.3,1)}
    .jfs-genre-count{font-size:.75rem;color:rgba(255,255,255,.35);width:28px;text-align:right}
    .jfs-recent-item{display:flex;align-items:center;gap:10px;padding:7px 0;border-bottom:1px solid rgba(255,255,255,.05)}
    .jfs-recent-item:last-child{border-bottom:none}
    .jfs-badge{font-size:.65rem;letter-spacing:.1em;padding:2px 6px;border-radius:3px;background:rgba(229,9,20,.15);color:#e50914;text-transform:uppercase;flex-shrink:0}
    .jfs-recent-name{font-size:.88rem;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .jfs-recent-date{font-size:.72rem;color:rgba(255,255,255,.3);flex-shrink:0}
    .jfs-show-row{display:flex;align-items:center;gap:10px;margin-bottom:9px}
    .jfs-show-rank{font-size:.85rem;font-weight:700;color:rgba(255,255,255,.25);width:18px;flex-shrink:0}
    .jfs-show-name{font-size:.88rem;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .jfs-show-count{font-size:.75rem;color:#e50914;flex-shrink:0}
    .jfs-progress-row{margin-bottom:9px}
    .jfs-progress-meta{display:flex;justify-content:space-between;font-size:.78rem;margin-bottom:4px}
    .jfs-progress-pct{color:#e50914}
    .jfs-progress-bar-wrap{height:3px;background:rgba(255,255,255,.07);border-radius:3px}
    .jfs-progress-bar{height:100%;border-radius:3px;background:linear-gradient(90deg,#a00008,#e50914)}
    .jfs-ring-wrap{display:flex;align-items:center;gap:20px}
    .jfs-ring-big{font-size:2.4rem;font-weight:700;line-height:1}
    .jfs-ring-sub{font-size:.78rem;color:rgba(255,255,255,.4);letter-spacing:.05em}
    .jfs-ring-detail{font-size:.8rem;color:rgba(255,255,255,.5);margin-top:10px;line-height:1.6}
    .jfs-mini-cards{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}
    .jfs-mini-card{background:rgba(255,255,255,.04);border:1px solid rgba(255,255,255,.07);border-radius:6px;padding:12px}
    .jfs-mini-val{font-size:1.5rem;font-weight:700;line-height:1}
    .jfs-mini-val span{font-size:.75rem;color:rgba(255,255,255,.35);font-weight:400;margin-left:2px}
    .jfs-mini-sub{font-size:.7rem;color:rgba(255,255,255,.35);margin-top:3px}
    .jfs-people-row{display:flex;align-items:center;gap:8px;margin-bottom:8px}
    .jfs-people-rank{font-size:.75rem;font-weight:700;color:rgba(255,255,255,.2);width:16px;flex-shrink:0}
    .jfs-people-name{font-size:.83rem;flex:1;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .jfs-people-bar-wrap{flex:1.5;height:3px;background:rgba(255,255,255,.07);border-radius:3px}
    .jfs-people-bar{height:100%;border-radius:3px;background:linear-gradient(90deg,#700,#e50914)}
    .jfs-people-count{font-size:.68rem;color:rgba(255,255,255,.3);width:50px;text-align:right}
    .jfs-leader-row{display:flex;align-items:center;gap:12px;padding:9px 0;border-bottom:1px solid rgba(255,255,255,.05)}
    .jfs-leader-row:last-child{border-bottom:none}
    .jfs-leader-rank{font-size:1.1rem;font-weight:700;color:rgba(255,255,255,.2);width:22px;flex-shrink:0}
    .jfs-leader-rank.gold{color:#f5c518}.jfs-leader-rank.silver{color:#aaa}.jfs-leader-rank.bronze{color:#cd7f32}
    .jfs-leader-name{flex:1;font-size:.93rem}
    .jfs-leader-bar-wrap{flex:2;height:4px;background:rgba(255,255,255,.08);border-radius:4px;overflow:hidden}
    .jfs-leader-bar{height:100%;border-radius:4px;background:linear-gradient(90deg,#a00008,#e50914)}
    .jfs-leader-hrs{font-size:.78rem;color:rgba(255,255,255,.4);width:52px;text-align:right;flex-shrink:0}
    .jfs-loading{text-align:center;padding:50px 0;color:rgba(255,255,255,.3);font-size:1rem;letter-spacing:.2em}
    .jfs-empty{color:rgba(255,255,255,.3);font-size:.85rem}
    #${BTN_ID}{display:flex;align-items:center;gap:10px;padding:10px 20px;cursor:pointer;color:rgba(255,255,255,.7);font-family:'Rajdhani',sans-serif;font-size:1rem;letter-spacing:.05em;transition:color .2s,background .2s;border-radius:4px;margin:2px 8px;border:none;background:transparent;width:calc(100% - 16px);text-align:left}
    #${BTN_ID}:hover{color:#fff;background:rgba(229,9,20,.1)}
  `;
  document.head.appendChild(style);

  // ── Render helpers ─────────────────────────────────────────────────────────
  function renderServerBanner(el, auth) {
    el.innerHTML = '<div class="jfs-loading">LOADING</div>';
    Promise.all([
      jellyApi('/System/Info', auth),
      jellyApi('/Items?Recursive=true&IncludeItemTypes=Movie&Limit=0', auth),
      jellyApi('/Items?Recursive=true&IncludeItemTypes=Series&Limit=0', auth),
      jellyApi('/Items?Recursive=true&IncludeItemTypes=Episode&Limit=0', auth),
    ]).then(([info, movies, shows, eps]) => {
      el.innerHTML = `
        <div class="jfs-server-card"><div class="jfs-server-card-label">Version</div><div class="jfs-server-card-value" style="font-size:1.1rem">${info.Version}</div></div>
        <div class="jfs-server-card"><div class="jfs-server-card-label">Movies</div><div class="jfs-server-card-value">${(movies.TotalRecordCount||0).toLocaleString()}<span>films</span></div></div>
        <div class="jfs-server-card"><div class="jfs-server-card-label">TV Shows</div><div class="jfs-server-card-value">${(shows.TotalRecordCount||0).toLocaleString()}<span>series</span></div></div>
        <div class="jfs-server-card"><div class="jfs-server-card-label">Episodes</div><div class="jfs-server-card-value">${(eps.TotalRecordCount||0).toLocaleString()}<span>eps</span></div></div>
      `;
    }).catch(() => { el.innerHTML = ''; });
  }

  function renderStatCards(el, summary) {
    const hrs = Math.round(summary.totalWatchTimeTicks / 36_000_000_000);
    const days = Math.round(hrs / 24);
    el.innerHTML = `
      <div class="jfs-card"><div class="jfs-card-label">Watch Time</div><div class="jfs-card-value">${hrs}<span>hrs</span></div><div class="jfs-card-sub">${days} days total</div></div>
      <div class="jfs-card"><div class="jfs-card-label">Movies</div><div class="jfs-card-value">${summary.moviesWatched}<span>films</span></div></div>
      <div class="jfs-card"><div class="jfs-card-label">Episodes</div><div class="jfs-card-value">${summary.episodesWatched}<span>eps</span></div></div>
      <div class="jfs-card"><div class="jfs-card-label">Total Titles</div><div class="jfs-card-value">${summary.moviesWatched + summary.episodesWatched}<span>items</span></div></div>
      <div class="jfs-card"><div class="jfs-card-label">Shows Started</div><div class="jfs-card-value">${summary.showsStarted}<span>series</span></div></div>
      <div class="jfs-card"><div class="jfs-card-label">Completed</div><div class="jfs-card-value">${summary.showsCompleted}<span>series</span></div></div>
    `;
  }

  function renderActivityChart(el, auth, userId, groupBy) {
    el.innerHTML = '<div class="jfs-loading">LOADING</div>';
    statsApi(`/Stats/user/${userId}/activity?groupBy=${groupBy}`, auth).then(buckets => {
      const max = Math.max(...buckets.map(b => b.count), 1);
      el.innerHTML = `
        <div class="jfs-chart-controls">
          ${['day','week','month','year'].map(g =>
            `<button class="jfs-chart-btn ${g===groupBy?'active':''}" data-group="${g}">${g.charAt(0).toUpperCase()+g.slice(1)}</button>`
          ).join('')}
        </div>
        <div class="jfs-chart-wrap">
          ${buckets.map(b => `
            <div class="jfs-chart-col">
              <div class="jfs-chart-bar" style="height:${Math.max(Math.round(b.count/max*100),2)}%"></div>
              <div class="jfs-chart-lbl">${b.label}</div>
            </div>`).join('')}
        </div>
      `;
      el.querySelectorAll('.jfs-chart-btn').forEach(btn => {
        btn.onclick = () => renderActivityChart(el, auth, userId, btn.dataset.group);
      });
    }).catch(e => { el.innerHTML = `<div class="jfs-empty">Could not load activity: ${e.message}</div>`; });
  }

  function renderGenres(el, auth, userId) {
    statsApi(`/Stats/user/${userId}/genres?limit=6`, auth).then(genres => {
      if (!genres.length) { el.innerHTML = '<div class="jfs-empty">No genre data yet</div>'; return; }
      const max = genres[0].count;
      el.innerHTML = genres.map(g => `
        <div class="jfs-genre-row">
          <div class="jfs-genre-name">${g.name}</div>
          <div class="jfs-genre-bar-wrap"><div class="jfs-genre-bar" style="width:${Math.round(g.count/max*100)}%"></div></div>
          <div class="jfs-genre-count">${g.count}</div>
        </div>`).join('');
    }).catch(() => { el.innerHTML = '<div class="jfs-empty">No genre data</div>'; });
  }

  function renderRecent(el, auth, userId) {
    statsApi(`/Stats/user/${userId}/recent?limit=20`, auth).then(items => {
      if (!items.length) { el.innerHTML = '<div class="jfs-empty">Nothing watched yet</div>'; return; }
      el.innerHTML = items.map(i => `
        <div class="jfs-recent-item">
          <div class="jfs-badge">${i.type === 'Movie' ? 'Film' : 'EP'}</div>
          <div class="jfs-recent-name">${i.seriesName ? i.seriesName + ' — ' : ''}${i.name}</div>
          <div class="jfs-recent-date">${timeAgo(i.lastPlayedDate)}</div>
        </div>`).join('');
    }).catch(() => { el.innerHTML = '<div class="jfs-empty">No recent data</div>'; });
  }

  function renderShows(favEl, completionEl, auth, userId) {
    statsApi(`/Stats/user/${userId}/shows`, auth).then(shows => {
      const top = shows.slice(0, 6);
      favEl.innerHTML = top.length
        ? top.map((s, i) => `
          <div class="jfs-show-row">
            <div class="jfs-show-rank">${i+1}</div>
            <div class="jfs-show-name">${s.name}</div>
            <div class="jfs-show-count">${s.episodesWatched} eps</div>
          </div>`).join('')
        : '<div class="jfs-empty">No episode data yet</div>';

      const started = shows.length;
      const completed = shows.filter(s => s.completed).length;
      const pct = started > 0 ? Math.round(completed / started * 100) : 0;
      const C = 2 * Math.PI * 36;
      const offset = C * (1 - pct / 100);
      const topProgress = shows.slice(0, 5);
      completionEl.innerHTML = `
        <div class="jfs-ring-wrap">
          <svg width="90" height="90" viewBox="0 0 90 90">
            <circle cx="45" cy="45" r="36" fill="none" stroke="rgba(255,255,255,.07)" stroke-width="7"/>
            <circle cx="45" cy="45" r="36" fill="none" stroke="#e50914" stroke-width="7"
              stroke-dasharray="${C.toFixed(1)}" stroke-dashoffset="${offset.toFixed(1)}"
              stroke-linecap="round" transform="rotate(-90 45 45)"/>
            <text x="45" y="50" text-anchor="middle" fill="#fff" font-family="Rajdhani,sans-serif" font-size="16" font-weight="700">${pct}%</text>
          </svg>
          <div>
            <div class="jfs-ring-sub">COMPLETION RATE</div>
            <div class="jfs-ring-big">${pct}%</div>
            <div class="jfs-ring-detail">${completed} completed<br>${started} started<br>${started - completed} in progress</div>
          </div>
        </div>
        <div style="margin-top:14px">
          ${topProgress.map(s => `
            <div class="jfs-progress-row">
              <div class="jfs-progress-meta"><span>${s.name}</span><span class="jfs-progress-pct">${s.completionPercent}%</span></div>
              <div class="jfs-progress-bar-wrap"><div class="jfs-progress-bar" style="width:${s.completionPercent}%"></div></div>
            </div>`).join('')}
        </div>`;
    }).catch(() => {
      favEl.innerHTML = '<div class="jfs-empty">No show data</div>';
      completionEl.innerHTML = '<div class="jfs-empty">No show data</div>';
    });
  }

  function renderBinge(el, auth, userId) {
    statsApi(`/Stats/user/${userId}/binge`, auth).then(b => {
      el.innerHTML = `
        <div class="jfs-mini-cards">
          <div class="jfs-mini-card"><div class="jfs-card-label">Longest Binge</div><div class="jfs-mini-val">${b.longestBingeEpisodes}<span>eps</span></div></div>
          <div class="jfs-mini-card"><div class="jfs-card-label">Best Session</div><div class="jfs-mini-val">${b.longestSessionHours}<span>hrs</span></div></div>
          <div class="jfs-mini-card"><div class="jfs-card-label">Avg Session</div><div class="jfs-mini-val">${b.averageSessionHours}<span>hrs</span></div></div>
        </div>`;
    }).catch(() => { el.innerHTML = '<div class="jfs-empty">No binge data</div>'; });
  }

  function renderPeople(actorEl, directorEl, auth, userId) {
    Promise.all([
      statsApi(`/Stats/user/${userId}/people?type=Actor&limit=10`, auth),
      statsApi(`/Stats/user/${userId}/people?type=Director&limit=10`, auth),
    ]).then(([actors, directors]) => {
      const renderList = (items, el) => {
        if (!items.length) { el.innerHTML = '<div class="jfs-empty">No data</div>'; return; }
        const max = items[0].titleCount;
        el.innerHTML = items.map((p, i) => `
          <div class="jfs-people-row">
            <div class="jfs-people-rank">${i+1}</div>
            <div class="jfs-people-name">${p.name}</div>
            <div class="jfs-people-bar-wrap"><div class="jfs-people-bar" style="width:${Math.round(p.titleCount/max*100)}%"></div></div>
            <div class="jfs-people-count">${p.titleCount} titles</div>
          </div>`).join('');
      };
      renderList(actors, actorEl);
      renderList(directors, directorEl);
    }).catch(() => {
      actorEl.innerHTML = '<div class="jfs-empty">No actor data</div>';
      directorEl.innerHTML = '<div class="jfs-empty">No director data</div>';
    });
  }

  function renderLeaderboard(el, auth) {
    statsApi('/Stats/leaderboard', auth).then(entries => {
      if (!entries.length) { el.innerHTML = '<div class="jfs-empty">No users</div>'; return; }
      const max = entries[0].totalHours || 1;
      const medals = ['gold','silver','bronze'];
      el.innerHTML = entries.map((e, i) => `
        <div class="jfs-leader-row">
          <div class="jfs-leader-rank ${medals[i]||''}">${i+1}</div>
          <div class="jfs-leader-name">${e.userName}</div>
          <div class="jfs-leader-bar-wrap"><div class="jfs-leader-bar" style="width:${Math.round(e.totalHours/max*100)}%"></div></div>
          <div class="jfs-leader-hrs">${e.totalHours} hrs</div>
        </div>`).join('');
    }).catch(() => { el.innerHTML = '<div class="jfs-empty">No leaderboard data</div>'; });
  }

  // ── Dashboard loader ────────────────────────────────────────────────────────
  async function loadUserDashboard(auth, userId, container) {
    container.innerHTML = '<div class="jfs-loading">LOADING STATS</div>';
    try {
      const summary = await statsApi(`/Stats/user/${userId}/summary`, auth);
      container.innerHTML = `
        <div id="jfs-stat-cards" class="jfs-cards"></div>
        <div class="jfs-section" id="jfs-activity-section"></div>
        <div class="jfs-2col">
          <div class="jfs-section">
            <div class="jfs-section-title">Top Genres</div>
            <div id="jfs-genres"></div>
          </div>
          <div class="jfs-section">
            <div class="jfs-section-title">Recently Watched</div>
            <div id="jfs-recent"></div>
          </div>
        </div>
        <div class="jfs-2col">
          <div class="jfs-section">
            <div class="jfs-section-title">Favourite Shows</div>
            <div id="jfs-fav-shows"></div>
          </div>
          <div class="jfs-section">
            <div class="jfs-section-title">Show Completion</div>
            <div id="jfs-completion"></div>
          </div>
        </div>
        <div class="jfs-section">
          <div class="jfs-section-title">Binge Stats</div>
          <div id="jfs-binge"></div>
        </div>
        <div class="jfs-2col">
          <div class="jfs-section">
            <div class="jfs-section-title">Top Actors</div>
            <div id="jfs-actors"></div>
          </div>
          <div class="jfs-section">
            <div class="jfs-section-title">Top Directors</div>
            <div id="jfs-directors"></div>
          </div>
        </div>
      `;

      renderStatCards(container.querySelector('#jfs-stat-cards'), summary);

      const actSection = container.querySelector('#jfs-activity-section');
      actSection.innerHTML = '<div class="jfs-section-title">Watch Activity</div><div id="jfs-activity-chart"></div>';
      renderActivityChart(actSection.querySelector('#jfs-activity-chart'), auth, userId, 'month');

      renderGenres(container.querySelector('#jfs-genres'), auth, userId);
      renderRecent(container.querySelector('#jfs-recent'), auth, userId);
      renderShows(
        container.querySelector('#jfs-fav-shows'),
        container.querySelector('#jfs-completion'),
        auth, userId
      );
      renderBinge(container.querySelector('#jfs-binge'), auth, userId);
      renderPeople(
        container.querySelector('#jfs-actors'),
        container.querySelector('#jfs-directors'),
        auth, userId
      );
    } catch (e) {
      container.innerHTML = `<div class="jfs-loading" style="color:#e50914">Error: ${e.message}</div>`;
    }
  }

  // ── Open stats page ─────────────────────────────────────────────────────────
  async function openStatsPage() {
    let page = document.getElementById(PAGE_ID);
    if (page) { page.classList.add('open'); return; }

    const auth = getAuth();
    if (!auth) { alert('Jellyfin Stats: Could not find auth credentials. Try refreshing.'); return; }

    const [cfg, me] = await Promise.all([
      statsApi('/Stats/config', auth).catch(() => ({ pluginTitle: 'Stats', leaderboardVisibleToAll: false })),
      jellyApi(`/Users/${auth.userId}`, auth).catch(() => ({})),
    ]);
    const isAdmin = !!me.Policy?.IsAdministrator;
    const title = cfg.pluginTitle || 'Stats';

    page = document.createElement('div');
    page.id = PAGE_ID;
    page.classList.add('open');

    const inner = document.createElement('div');
    inner.className = 'jfs-inner';
    inner.innerHTML = `
      <div class="jfs-header">
        <div class="jfs-title">${title.toUpperCase().replace(/^(\w)/, '<span>$1</span>')}</div>
        <button class="jfs-close" id="jfs-close-btn">✕ CLOSE</button>
      </div>
      <div id="jfs-banner" class="jfs-server-banner"></div>
      <div id="jfs-tabs-wrap"></div>
      <div id="jfs-leaderboard-wrap"></div>
      <div id="jfs-content"></div>
    `;
    page.appendChild(inner);
    document.body.appendChild(page);

    document.getElementById('jfs-close-btn').onclick = () => page.classList.remove('open');

    const contentEl  = inner.querySelector('#jfs-content');
    const leaderEl   = inner.querySelector('#jfs-leaderboard-wrap');
    const tabsWrap   = inner.querySelector('#jfs-tabs-wrap');
    const bannerEl   = inner.querySelector('#jfs-banner');

    renderServerBanner(bannerEl, auth);

    if (isAdmin || cfg.leaderboardVisibleToAll) {
      const lSection = document.createElement('div');
      lSection.className = 'jfs-section';
      lSection.innerHTML = '<div class="jfs-section-title">Leaderboard — Most Watched</div><div id="jfs-leader-list"></div>';
      leaderEl.appendChild(lSection);
      renderLeaderboard(lSection.querySelector('#jfs-leader-list'), auth);
    }

    if (isAdmin) {
      try {
        const users = await jellyApi('/Users', auth);
        const tabsDiv = document.createElement('div');
        tabsDiv.className = 'jfs-tabs';

        users.forEach(u => {
          const tab = document.createElement('button');
          tab.className = 'jfs-tab' + (u.Id === auth.userId ? ' active' : '');
          tab.textContent = u.Id === auth.userId ? 'My Stats' : u.Name;
          tab.dataset.userId = u.Id;
          tabsDiv.appendChild(tab);
        });

        tabsWrap.appendChild(tabsDiv);
        tabsDiv.addEventListener('click', e => {
          const tab = e.target.closest('.jfs-tab');
          if (!tab) return;
          tabsDiv.querySelectorAll('.jfs-tab').forEach(t => t.classList.remove('active'));
          tab.classList.add('active');
          loadUserDashboard(auth, tab.dataset.userId, contentEl);
        });
      } catch {}
    }

    loadUserDashboard(auth, auth.userId, contentEl);
  }

  // ── Sidebar button injection ────────────────────────────────────────────────
  function injectBtn() {
    if (document.getElementById(BTN_ID)) return;
    const nav = document.querySelector(
      '.mainDrawer-scrollContainer, .navMenuOption:last-of-type, .sidebarLinks'
    );
    if (!nav) return;

    const btn = document.createElement('button');
    btn.id = BTN_ID;
    btn.innerHTML = `
      <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M18 20V10M12 20V4M6 20v-6"/>
      </svg>Statistics`;
    btn.addEventListener('click', openStatsPage);
    nav.appendChild(btn);
  }

  const obs = new MutationObserver(injectBtn);
  obs.observe(document.body, { childList: true, subtree: true });
  setTimeout(injectBtn, 2000);

})();
