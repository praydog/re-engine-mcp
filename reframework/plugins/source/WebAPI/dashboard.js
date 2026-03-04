const API_BASE = window.location.origin;
let pollCount = 0;

// Health slider state
let isDraggingHealth = false;
let lastKnownMaxHealth = 100;

function row(label, value, cls = '') {
  return `<div class='row'><span class='label'>${label}</span><span class='value ${cls}'>${value}</span></div>`;
}

function fmt(n, d = 2) {
  return n != null ? Number(n).toFixed(d) : '?';
}

function esc(s) { return s ? s.replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;') : ''; }

function formatPlayTime(s) {
  if (s == null) return '?';
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return `${h}h ${m}m`;
}

function hpColor(pct) {
  if (pct > 0.6) return '#3fb950';
  if (pct > 0.3) return '#d29922';
  return '#f85149';
}

function setDot(cardId, ok) {
  const dot = document.querySelector(`#${cardId} .dot`);
  if (dot) dot.className = ok ? 'dot' : 'dot error';
}

async function fetchJson(path) {
  const r = await fetch(API_BASE + path);
  return r.json();
}

async function setPlayerHealth(value) {
  await fetch(API_BASE + '/api/player/health', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ value })
  });
}

function updateHealthBar(health, maxHealth) {
  const pct = maxHealth > 0 ? health / maxHealth : 0;
  const color = hpColor(pct);
  const bar = document.getElementById('hp-bar-fill');
  const label = document.getElementById('hp-bar-label');
  if (bar) {
    bar.style.width = (pct * 100) + '%';
    bar.style.background = color;
  }
  if (label) {
    label.textContent = `${fmt(health, 0)} / ${fmt(maxHealth, 0)}`;
  }
}

function onHealthSliderInput(e) {
  const val = parseFloat(e.target.value);
  updateHealthBar(val, lastKnownMaxHealth);
}

function onHealthSliderDown() {
  isDraggingHealth = true;
}

function onHealthSliderUp(e) {
  isDraggingHealth = false;
  const val = parseFloat(e.target.value);
  setPlayerHealth(val);
}

async function updatePlayer() {
  try {
    const d = await fetchJson('/api/player');
    const el = document.getElementById('player-content');
    setDot('player-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    // Detect game by checking for game-specific fields
    if (d.playerType !== undefined) {
      updatePlayerRE2(d, el);
    } else if (d.characterId !== undefined) {
      updatePlayerRE9(d, el);
    } else {
      updatePlayerMHWilds(d, el);
    }
  } catch(e) {
    setDot('player-card', false);
    document.getElementById('player-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

function updatePlayerRE9(d, el) {
  if (d.maxHealth > 0) lastKnownMaxHealth = d.maxHealth;

  const slider = document.getElementById('hp-slider');
  if (!slider) {
    const statusBadges = [];
    if (d.isInSafeRoom) statusBadges.push("<span class='badge badge-safe'>Safe Room</span>");
    if (d.isDead) statusBadges.push("<span class='badge badge-dead'>Dead</span>");
    if (d.invincible) statusBadges.push("<span class='badge badge-invincible'>Invincible</span>");
    if (d.isGameOver) statusBadges.push("<span class='badge badge-dead'>Game Over</span>");

    el.innerHTML =
      row('Character', d.characterName || d.characterId || '?', 'highlight') +
      (statusBadges.length ? `<div class='status-badges'>${statusBadges.join(' ')}</div>` : '') +
      `<div class='hp-bar-bg'>
        <div class='hp-bar' id='hp-bar-fill'><span id='hp-bar-label'></span></div>
      </div>
      <input type='range' id='hp-slider' class='hp-slider' min='0' max='${d.maxHealth || 100}' step='1' value='${d.health || 0}'>` +
      row('Position', `${fmt(d.position.x)}, ${fmt(d.position.y)}, ${fmt(d.position.z)}`);

    const newSlider = document.getElementById('hp-slider');
    newSlider.addEventListener('input', onHealthSliderInput);
    newSlider.addEventListener('pointerdown', onHealthSliderDown);
    newSlider.addEventListener('pointerup', onHealthSliderUp);
  } else {
    if (d.maxHealth && parseFloat(slider.max) !== d.maxHealth) slider.max = d.maxHealth;
    if (!isDraggingHealth) slider.value = d.health;

    // Update status badges
    const badgesEl = el.querySelector('.status-badges');
    if (badgesEl) {
      const statusBadges = [];
      if (d.isInSafeRoom) statusBadges.push("<span class='badge badge-safe'>Safe Room</span>");
      if (d.isDead) statusBadges.push("<span class='badge badge-dead'>Dead</span>");
      if (d.invincible) statusBadges.push("<span class='badge badge-invincible'>Invincible</span>");
      if (d.isGameOver) statusBadges.push("<span class='badge badge-dead'>Game Over</span>");
      badgesEl.innerHTML = statusBadges.join(' ');
    }

    const rows = el.querySelectorAll('.row');
    for (const r of rows) {
      const lbl = r.querySelector('.label')?.textContent;
      const val = r.querySelector('.value');
      if (!val) continue;
      if (lbl === 'Position') val.textContent = `${fmt(d.position.x)}, ${fmt(d.position.y)}, ${fmt(d.position.z)}`;
    }
  }

  if (!isDraggingHealth) updateHealthBar(d.health, d.maxHealth);
}

function updatePlayerMHWilds(d, el) {
    if (d.maxHealth > 0) lastKnownMaxHealth = d.maxHealth;

    // Build health slider row — only rebuild DOM if slider doesn't exist yet
    const slider = document.getElementById('hp-slider');
    if (!slider) {
      el.innerHTML =
        row('Name', d.name || '?', 'highlight') +
        row('Level', d.level) +
        (d.otomoName ? row('Palico', d.otomoName) : '') +
        (d.seikretName ? row('Seikret', d.seikretName) : '') +
        (d.zenny != null ? row('Zenny', d.zenny.toLocaleString() + 'z') : '') +
        (d.points != null ? row('Points', d.points.toLocaleString() + 'p') : '') +
        (d.playTimeSeconds != null ? row('Play Time', formatPlayTime(d.playTimeSeconds)) : '') +
        `<div class='hp-bar-bg'>
          <div class='hp-bar' id='hp-bar-fill'><span id='hp-bar-label'></span></div>
        </div>
        <input type='range' id='hp-slider' class='hp-slider' min='0' max='${d.maxHealth || 100}' step='1' value='${d.health || 0}'>` +
        row('Position', `${fmt(d.position.x)}, ${fmt(d.position.y)}, ${fmt(d.position.z)}`) +
        row('Dist to Camera', fmt(d.distToCamera));

      // Attach events after DOM insertion
      const newSlider = document.getElementById('hp-slider');
      newSlider.addEventListener('input', onHealthSliderInput);
      newSlider.addEventListener('pointerdown', onHealthSliderDown);
      newSlider.addEventListener('pointerup', onHealthSliderUp);
    } else {
      // Update slider max if it changed
      if (d.maxHealth && parseFloat(slider.max) !== d.maxHealth) {
        slider.max = d.maxHealth;
      }

      // Only update slider value if user isn't dragging
      if (!isDraggingHealth) {
        slider.value = d.health;
      }

      // Update non-slider rows by label instead of index
      const rows = el.querySelectorAll('.row');
      for (const r of rows) {
        const lbl = r.querySelector('.label')?.textContent;
        const val = r.querySelector('.value');
        if (!val) continue;
        if (lbl === 'Name') val.textContent = d.name || '?';
        else if (lbl === 'Level') val.textContent = d.level;
        else if (lbl === 'Zenny') val.textContent = d.zenny != null ? d.zenny.toLocaleString() + 'z' : '?';
        else if (lbl === 'Points') val.textContent = d.points != null ? d.points.toLocaleString() + 'p' : '?';
        else if (lbl === 'Play Time') val.textContent = d.playTimeSeconds != null ? formatPlayTime(d.playTimeSeconds) : '?';
        else if (lbl === 'Position') val.textContent = `${fmt(d.position.x)}, ${fmt(d.position.y)}, ${fmt(d.position.z)}`;
        else if (lbl === 'Dist to Camera') val.textContent = fmt(d.distToCamera);
      }
    }

    // Always update the visual bar (unless dragging, which handles it via onHealthSliderInput)
    if (!isDraggingHealth) {
      updateHealthBar(d.health, d.maxHealth);
    }
}

function updatePlayerRE2(d, el) {
  if (d.maxHealth > 0) lastKnownMaxHealth = d.maxHealth;

  const slider = document.getElementById('hp-slider');
  if (!slider) {
    const statusBadges = [];
    if (d.isDead) statusBadges.push("<span class='badge badge-dead'>Dead</span>");
    if (d.status && d.status.isPoison) statusBadges.push("<span class='badge badge-dead'>Poison</span>");
    if (d.status && d.status.isCombat) statusBadges.push("<span class='badge badge-elite'>Combat</span>");
    if (d.status && d.status.isEvent) statusBadges.push("<span class='badge badge-safe'>Event</span>");
    if (d.status && d.status.isAttacked) statusBadges.push("<span class='badge badge-dead'>Attacked</span>");

    const vitalClean = d.status && d.status.vital ? d.status.vital.split(' ')[0] : '?';
    const weaponClean = d.equipment && d.equipment.weapon ? d.equipment.weapon.split(' ')[0] : '?';

    el.innerHTML =
      row('Character', d.playerName || '?', 'highlight') +
      row('Type', d.playerType || '?') +
      (statusBadges.length ? `<div class='status-badges'>${statusBadges.join(' ')}</div>` : '') +
      `<div class='hp-bar-bg'>
        <div class='hp-bar' id='hp-bar-fill'><span id='hp-bar-label'></span></div>
      </div>
      <input type='range' id='hp-slider' class='hp-slider' min='0' max='${d.maxHealth || 1200}' step='1' value='${d.health || 0}'>` +
      row('Vital', vitalClean) +
      row('Weapon', weaponClean) +
      row('Position', `${fmt(d.position.x)}, ${fmt(d.position.y)}, ${fmt(d.position.z)}`);

    const newSlider = document.getElementById('hp-slider');
    newSlider.addEventListener('input', onHealthSliderInput);
    newSlider.addEventListener('pointerdown', onHealthSliderDown);
    newSlider.addEventListener('pointerup', onHealthSliderUp);
  } else {
    if (d.maxHealth && parseFloat(slider.max) !== d.maxHealth) slider.max = d.maxHealth;
    if (!isDraggingHealth) slider.value = d.health;

    // Update status badges
    const badgesEl = el.querySelector('.status-badges');
    if (badgesEl) {
      const statusBadges = [];
      if (d.isDead) statusBadges.push("<span class='badge badge-dead'>Dead</span>");
      if (d.status && d.status.isPoison) statusBadges.push("<span class='badge badge-dead'>Poison</span>");
      if (d.status && d.status.isCombat) statusBadges.push("<span class='badge badge-elite'>Combat</span>");
      if (d.status && d.status.isEvent) statusBadges.push("<span class='badge badge-safe'>Event</span>");
      if (d.status && d.status.isAttacked) statusBadges.push("<span class='badge badge-dead'>Attacked</span>");
      badgesEl.innerHTML = statusBadges.join(' ');
    }

    const vitalClean = d.status && d.status.vital ? d.status.vital.split(' ')[0] : '?';
    const weaponClean = d.equipment && d.equipment.weapon ? d.equipment.weapon.split(' ')[0] : '?';

    const rows = el.querySelectorAll('.row');
    for (const r of rows) {
      const lbl = r.querySelector('.label')?.textContent;
      const val = r.querySelector('.value');
      if (!val) continue;
      if (lbl === 'Character') val.textContent = d.playerName || '?';
      else if (lbl === 'Position') val.textContent = `${fmt(d.position.x)}, ${fmt(d.position.y)}, ${fmt(d.position.z)}`;
      else if (lbl === 'Vital') val.textContent = vitalClean;
      else if (lbl === 'Weapon') val.textContent = weaponClean;
    }
  }

  if (!isDraggingHealth) updateHealthBar(d.health, d.maxHealth);
}
async function updateCamera() {
  try {
    const d = await fetchJson('/api/camera');
    const el = document.getElementById('camera-content');
    setDot('camera-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }
    el.innerHTML =
      row('Position', `${fmt(d.position.x)}, ${fmt(d.position.y)}, ${fmt(d.position.z)}`) +
      row('FOV', fmt(d.fov, 1) + '\u00B0') +
      row('Near Clip', fmt(d.nearClip, 3)) +
      row('Far Clip', fmt(d.farClip, 1));
  } catch(e) {
    setDot('camera-card', false);
  }
}

async function updateLobby() {
  try {
    const d = await fetchJson('/api/lobby');
    const el = document.getElementById('lobby-content');
    setDot('lobby-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    document.getElementById('lobby-count').textContent = `(${d.count})`;

    d.members.sort((a, b) => a.name.localeCompare(b.name));

    el.innerHTML = d.members.map(m => {
      const cls = m.isSelf ? 'is-self' : m.isQuest ? 'is-quest' : '';
      const hr = m.hunterRank > 0 ? `<span class='lobby-hr'>HR${m.hunterRank}</span>` : '';
      return `<div class='lobby-member ${cls}'>
        <span class='lobby-name' title='${m.name}'>${m.name}</span>
        ${hr}
      </div>`;
    }).join('');
  } catch(e) {
    setDot('lobby-card', false);
    document.getElementById('lobby-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── FPS graph ──────────────────────────────────────────────────────
const FPS_HISTORY_SIZE = 120;
const fpsHistory = [];

async function updateFPS() {
  try {
    const d = await fetchJson('/api/camera'); // cheap call we already make
    // deltaTime comes from via.Application — let's use a batch to get it
    const app = await fetchJson('/api/explorer/singleton?typeName=via.Application');
    if (app.error) { setDot('fps-card', false); return; }

    const batch = await (await fetch(API_BASE + '/api/explorer/batch', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ operations: [
        { type: 'method', params: { address: app.address, kind: app.kind, typeName: app.typeName, methodName: 'get_DeltaTime' } },
        { type: 'method', params: { address: app.address, kind: app.kind, typeName: app.typeName, methodName: 'get_FrameCount' } }
      ]})
    })).json();

    const dt = parseFloat(batch.results[0].value);
    const fps = dt > 0 ? 1.0 / dt : 0;

    fpsHistory.push(fps);
    if (fpsHistory.length > FPS_HISTORY_SIZE) fpsHistory.shift();

    document.getElementById('fps-value').textContent = `${fps.toFixed(1)} fps`;
    setDot('fps-card', true);
    drawFPSGraph();
  } catch(e) {
    setDot('fps-card', false);
  }
}

// Cache via.Application address and last frame count for FPS calculation
let appAddr = null;
let lastFrameCount = null;
let lastFrameTime = null;

async function updateFPSFast() {
  try {
    if (!appAddr) {
      const app = await fetchJson('/api/explorer/singleton?typeName=via.Application');
      if (app.error) { setDot('fps-card', false); return; }
      appAddr = app;
    }

    const resp = await fetch(API_BASE + '/api/explorer/method?' + new URLSearchParams({
      address: appAddr.address, kind: appAddr.kind, typeName: appAddr.typeName, methodName: 'get_FrameCount'
    }));
    const d = await resp.json();
    if (d.error) { appAddr = null; setDot('fps-card', false); return; }

    const frameCount = parseInt(d.value);
    const now = performance.now();

    if (lastFrameCount !== null && lastFrameTime !== null) {
      const dFrames = frameCount - lastFrameCount;
      const dTime = (now - lastFrameTime) / 1000;
      if (dTime > 0 && dFrames >= 0) {
        const fps = dFrames / dTime;

        fpsHistory.push(fps);
        if (fpsHistory.length > FPS_HISTORY_SIZE) fpsHistory.shift();

        document.getElementById('fps-value').textContent = `${fps.toFixed(1)} fps`;
        setDot('fps-card', true);
        drawFPSGraph();
      }
    }

    lastFrameCount = frameCount;
    lastFrameTime = now;
  } catch(e) {
    setDot('fps-card', false);
  }
}

function drawFPSGraph() {
  const canvas = document.getElementById('fps-canvas');
  if (!canvas) return;

  // Resize canvas to fill card width
  const rect = canvas.parentElement.getBoundingClientRect();
  canvas.width = rect.width - 40; // account for card padding

  const ctx = canvas.getContext('2d');
  const w = canvas.width;
  const h = canvas.height;

  ctx.clearRect(0, 0, w, h);

  if (fpsHistory.length < 2) return;

  const maxFps = Math.max(90, ...fpsHistory);
  const minFps = Math.min(...fpsHistory);

  // Draw target lines
  for (const target of [30, 60]) {
    if (target > maxFps) continue;
    const y = h - (target / maxFps) * h;
    ctx.strokeStyle = '#21262d';
    ctx.lineWidth = 1;
    ctx.setLineDash([4, 4]);
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(w, y);
    ctx.stroke();
    ctx.setLineDash([]);

    ctx.fillStyle = '#484f58';
    ctx.font = '10px monospace';
    ctx.fillText(target + '', 2, y - 2);
  }

  // Draw FPS line
  const step = w / (FPS_HISTORY_SIZE - 1);
  const startIdx = FPS_HISTORY_SIZE - fpsHistory.length;

  ctx.beginPath();
  for (let i = 0; i < fpsHistory.length; i++) {
    const x = (startIdx + i) * step;
    const y = h - (fpsHistory[i] / maxFps) * h;
    if (i === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.strokeStyle = '#58a6ff';
  ctx.lineWidth = 1.5;
  ctx.stroke();

  // Fill under the line
  const lastX = (startIdx + fpsHistory.length - 1) * step;
  const firstX = startIdx * step;
  ctx.lineTo(lastX, h);
  ctx.lineTo(firstX, h);
  ctx.closePath();
  ctx.fillStyle = 'rgba(88, 166, 255, 0.08)';
  ctx.fill();

  // Color the current value
  const current = fpsHistory[fpsHistory.length - 1];
  const dotColor = current >= 55 ? '#3fb950' : current >= 30 ? '#d29922' : '#f85149';
  const dotX = lastX;
  const dotY = h - (current / maxFps) * h;
  ctx.beginPath();
  ctx.arc(dotX, dotY, 3, 0, Math.PI * 2);
  ctx.fillStyle = dotColor;
  ctx.fill();
}

// ── Weather ────────────────────────────────────────────────────────

const weatherIcons = {
  Fertility: '🌿', Devastation: '💀', Abnormal: '⚠️', SandStorm: '🏜️',
  HeavyRain: '🌧️', Magma: '🌋', Blizzard: '❄️', Energy: '⚡',
  Junction: '🌀', LastBossPhase1: '👁️', LastBossPhase2: '👁️',
  Live: '🎵', MagmaBossPhase2: '🌋'
};

async function updateWeather() {
  try {
    const d = await fetchJson('/api/weather');
    const el = document.getElementById('weather-content');
    setDot('weather-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    const icon = weatherIcons[d.current] || '🌤️';
    const clockIcons = { MORNING: '🌅', NOON: '☀️', EVENING: '🌇', NIGHT: '🌙', MIDNIGHT: '🌑' };
    const clockIcon = clockIcons[d.timeZone] || '🕐';

    let html = `<div class='weather-current'><span class='weather-icon'>${icon}</span><span class='weather-name'>${d.current || '?'}</span></div>`;
    html += `<div class='weather-clock'>${clockIcon} ${d.clock || '??:??'} <span class='weather-timezone'>${d.timeZone || ''}</span></div>`;

    if (d.next && d.next !== 'None') {
      const nextIcon = weatherIcons[d.next] || '🌤️';
      html += `<div class='weather-next'>Transitioning to: ${nextIcon} ${d.next}</div>`;
    }

    // Show blend bars for active weather types
    const active = (d.blends || []).filter(b => b.blendRate > 0.001);
    if (active.length > 0) {
      html += `<div class='weather-blends'>`;
      for (const b of active) {
        const pct = (b.blendRate * 100).toFixed(0);
        const bIcon = weatherIcons[b.name] || '';
        html += `<div class='weather-blend-row'>
          <span class='weather-blend-label'>${bIcon} ${b.name}</span>
          <div class='weather-blend-bar-bg'><div class='weather-blend-bar' style='width:${pct}%'></div></div>
          <span class='weather-blend-pct'>${pct}%</span>
        </div>`;
      }
      html += `</div>`;
    }

    el.innerHTML = html;
  } catch(e) {
    setDot('weather-card', false);
  }
}

// ── Map / Location ─────────────────────────────────────────────────

function formatSeconds(s) {
  if (s == null) return '?';
  const m = Math.floor(s / 60);
  const sec = Math.floor(s % 60);
  return `${m}:${sec.toString().padStart(2, '0')}`;
}

async function updateMap() {
  try {
    const d = await fetchJson('/api/map');
    const el = document.getElementById('map-content');
    setDot('map-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    let html = row('Stage', `${d.stageName}`, 'highlight') +
               row('Stage Code', d.stage) +
               row('Area No.', d.areaNo);

    if (d.prevStage && d.prevStage !== 'INVALID') {
      html += row('Previous', d.prevStageName);
    }

    if (d.quest && d.quest.active) {
      html += `<div style='margin-top:8px;padding-top:8px;border-top:1px solid #21262d'>`;
      html += row('Quest', d.quest.id || '?', 'warn');
      if (d.quest.playing) {
        html += row('Status', 'In Progress', 'highlight');
        if (d.quest.remainTime != null) html += row('Time Left', formatSeconds(d.quest.remainTime), d.quest.remainTime < 300 ? 'warn' : '');
        if (d.quest.elapsedTime != null) html += row('Elapsed', formatSeconds(d.quest.elapsedTime));
      } else {
        html += row('Status', 'Accepted');
      }
      if (d.quest.beforeStage) html += row('Departed From', d.quest.beforeStage);
      html += `</div>`;
    }

    el.innerHTML = html;
  } catch(e) {
    setDot('map-card', false);
    document.getElementById('map-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Equipment ──────────────────────────────────────────────────────

const weaponIcons = {
  GREAT_SWORD: '🗡️', LONG_SWORD: '⚔️', SWORD_AND_SHIELD: '🛡️', DUAL_BLADES: '🔪',
  HAMMER: '🔨', HUNTING_HORN: '🎺', LANCE: '🔱', GUN_LANCE: '💥',
  SWITCH_AXE: '🪓', CHARGE_AXE: '⚡', INSECT_GLAIVE: '🦗', LIGHT_BOWGUN: '🔫',
  HEAVY_BOWGUN: '💣', BOW: '🏹'
};

const elementColors = {
  FIRE: '#f85149', WATER: '#58a6ff', THUNDER: '#d29922', ICE: '#79c0ff',
  DRAGON: '#a371f7', POISON: '#bc8cff', SLEEP: '#7ee787', PARALYSIS: '#e3b341',
  BLAST: '#f0883e'
};

function slotGems(slots) {
  if (!slots) return '';
  return slots.map(s => s > 0 ? `<span class='equip-gem equip-gem-${s}' title='Lv${s}'>${s}</span>` : '').join('');
}

async function updateEquipment() {
  try {
    const d = await fetchJson('/api/equipment');
    const el = document.getElementById('equip-content');
    setDot('equip-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    const w = d.weapon;
    const wIcon = weaponIcons[w.type] || '⚔️';
    const elemColor = elementColors[w.element] || '#8b949e';
    const hasElem = w.element && w.element !== 'NONE';
    const hasSub = w.subElement && w.subElement !== 'NONE';
    const rare = w.rarity ? w.rarity.replace('RARE', 'R') : '?';

    const wTip = w.description ? ` data-tip="${esc(w.description)}"` : '';
    let html = `<div class='equip-weapon'${wTip}>
      <span style='font-size:1.4em'>${wIcon}</span>
      <div style='flex:1'>
        <div class='equip-weapon-name'>${w.name}</div>
        <div class='equip-weapon-type'>${w.type} &middot; ${rare}</div>
      </div>
      <div class='equip-slots'>${slotGems(w.slots)}</div>
    </div>`;

    html += `<div class='equip-stats'>`;
    html += `<div class='equip-stat'><span class='equip-stat-label'>ATK</span><span class='equip-stat-val'>${w.attack}</span></div>`;
    html += `<div class='equip-stat'><span class='equip-stat-label'>Affinity</span><span class='equip-stat-val' style='color:${w.critical > 0 ? '#3fb950' : w.critical < 0 ? '#f85149' : '#c9d1d9'}'>${w.critical > 0 ? '+' : ''}${w.critical}%</span></div>`;
    if (hasElem) {
      html += `<div class='equip-stat'><span class='equip-stat-label' style='color:${elemColor}'>${w.element}</span><span class='equip-stat-val' style='color:${elemColor}'>${w.elementValue}</span></div>`;
    }
    if (hasSub) {
      const subColor = elementColors[w.subElement] || '#8b949e';
      html += `<div class='equip-stat'><span class='equip-stat-label' style='color:${subColor}'>${w.subElement}</span><span class='equip-stat-val' style='color:${subColor}'>${w.subElementValue}</span></div>`;
    }
    if (w.defense > 0) {
      html += `<div class='equip-stat'><span class='equip-stat-label'>DEF</span><span class='equip-stat-val'>+${w.defense}</span></div>`;
    }
    html += `</div>`;

    html += `<div class='equip-armor-list'>`;
    const slotIcons = { Helm: '🪖', Body: '👕', Arms: '🧤', Waist: '🩳', Legs: '🥾' };
    for (const a of d.armor) {
      const lvl = a.upgradeLevel > 0 ? `Lv${a.upgradeLevel}` : '';
      const icon = slotIcons[a.slot] || '';
      const aTip = a.description ? ` data-tip="${esc(a.description)}"` : '';
      html += `<div class='equip-armor-piece'${aTip}>
        <span class='equip-slot'>${icon} ${a.slot}</span>
        <span class='equip-name'>${a.name}</span>
        <span class='equip-level'>${lvl}</span>
      </div>`;
    }
    html += `</div>`;

    if (d.palico) {
      const p = d.palico;
      html += `<div style='margin-top:12px;padding-top:10px;border-top:1px solid #21262d'>
        <div style='color:#8b949e;font-size:0.8em;margin-bottom:6px'>Palico</div>
        <div class='equip-armor-list'>`;
      const pSlots = [
        { icon: '🗡️', label: 'Weapon', piece: p.weapon },
        { icon: '🪖', label: 'Helm', piece: p.helm },
        { icon: '👕', label: 'Body', piece: p.body }
      ];
      for (const s of pSlots) {
        const r = s.piece.rarity ? s.piece.rarity.replace('RARE', 'R') : '';
        const pTip = s.piece.description ? ` data-tip="${esc(s.piece.description)}"` : '';
        html += `<div class='equip-armor-piece'${pTip}>
          <span class='equip-slot'>${s.icon} ${s.label}</span>
          <span class='equip-name'>${s.piece.name}</span>
          <span class='equip-level'>${r}</span>
        </div>`;
      }
      html += `</div></div>`;
    }

    el.innerHTML = html;
  } catch(e) {
    setDot('equip-card', false);
    document.getElementById('equip-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Inventory ───────────────────────────────────────────────────────

async function updateInventory() {
  try {
    const d = await fetchJson('/api/inventory');
    const el = document.getElementById('inventory-content');
    setDot('inventory-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    document.getElementById('inventory-count').textContent = `(${d.count}/${d.capacity})`;

    if (!d.items || d.items.length === 0) {
      el.innerHTML = `<span class='error-msg'>Empty</span>`;
      return;
    }

    // Detect RE2 inventory (has isWeapon field)
    const isRE2 = d.items[0].isWeapon !== undefined;

    el.innerHTML = d.items.map(item => {
      if (!isRE2) {
        return `<div class='inv-item'>
          <span class='inv-name'>${esc(item.name)}</span>
          <span class='inv-qty'>x${item.quantity}</span>
        </div>`;
      }

      const nameClass = item.isWeapon ? 'inv-weapon' : 'inv-name';

      let qty = '';
      if (item.isWeapon && item.maxQuantity > 1) {
        // Weapon with ammo/durability bar
        const pct = item.maxQuantity > 0 ? (item.quantity / item.maxQuantity * 100).toFixed(0) : 0;
        const color = hpColor(item.remainRatio || 0);
        qty = `<div class='inv-bar-wrap'>
          <span class='inv-qty'>${item.quantity}/${item.maxQuantity}</span>
          <div class='inv-bar-bg'><div class='inv-bar' style='width:${pct}%;background:${color}'></div></div>
        </div>`;
      } else if (item.quantity > 1 || item.maxQuantity > 1) {
        qty = `<span class='inv-qty'>x${item.quantity}</span>`;
      }

      return `<div class='inv-item'>
        <span class='${nameClass}'>${esc(item.name)}</span>
        ${qty}
      </div>`;
    }).join('');
  } catch(e) {
    setDot('inventory-card', false);
    document.getElementById('inventory-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Mesh Visibility ─────────────────────────────────────────────────

async function toggleMesh(name, visible) {
  await fetch(API_BASE + '/api/meshes', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, visible })
  });
}

async function updateMeshes() {
  try {
    const d = await fetchJson('/api/meshes');
    const el = document.getElementById('mesh-content');
    setDot('mesh-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    if (!d.meshes || d.meshes.length === 0) {
      el.innerHTML = `<span class='error-msg'>No meshes found</span>`;
      return;
    }

    // Only rebuild DOM if mesh count changed
    const existing = el.querySelectorAll('.mesh-toggle');
    if (existing.length !== d.meshes.length) {
      el.innerHTML = d.meshes.map(m => {
        const checked = m.visible ? 'checked' : '';
        return `<label class='mesh-toggle'>
          <input type='checkbox' ${checked} data-mesh='${esc(m.name)}'>
          <span class='mesh-label'>${m.label}</span>
          <span class='mesh-name'>${m.name}</span>
        </label>`;
      }).join('');

      el.addEventListener('change', e => {
        const cb = e.target;
        if (cb.dataset.mesh) toggleMesh(cb.dataset.mesh, cb.checked);
      });
    } else {
      // Update checkbox states without rebuilding
      for (const m of d.meshes) {
        const cb = el.querySelector(`input[data-mesh="${m.name}"]`);
        if (cb && cb !== document.activeElement) cb.checked = m.visible;
      }
    }
  } catch(e) {
    setDot('mesh-card', false);
    document.getElementById('mesh-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Hunt Log ────────────────────────────────────────────────────────

async function updateHuntLog() {
  try {
    const d = await fetchJson('/api/huntlog');
    const el = document.getElementById('huntlog-content');
    setDot('huntlog-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    document.getElementById('huntlog-count').textContent = `(${d.count})`;

    if (!d.monsters || d.monsters.length === 0) {
      el.innerHTML = `<span class='error-msg'>No hunts recorded</span>`;
      return;
    }

    // Sort by total count descending
    d.monsters.sort((a, b) => b.totalCount - a.totalCount);

    el.innerHTML = d.monsters.map(m => {
      return `<div class='huntlog-row'>
        <span class='huntlog-name'>${esc(m.name)}</span>
        <span class='huntlog-counts'>
          <span class='huntlog-badge hunt' title='Hunted'>${m.huntCount}</span>
          <span class='huntlog-badge slay' title='Slain'>${m.slayCount}</span>
          <span class='huntlog-badge capture' title='Captured'>${m.captureCount}</span>
        </span>
      </div>`;
    }).join('');
  } catch(e) {
    setDot('huntlog-card', false);
    document.getElementById('huntlog-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Palico Stats ────────────────────────────────────────────────────

async function updatePalico() {
  try {
    const d = await fetchJson('/api/palico');
    const el = document.getElementById('palico-content');
    setDot('palico-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    const pct = d.maxHealth > 0 ? d.health / d.maxHealth : 0;
    const color = hpColor(pct);

    let html = row('Level', d.level ?? '?', 'highlight');
    html += `<div class='hp-bar-bg'><div class='hp-bar' style='width:${(pct * 100).toFixed(0)}%;background:${color}'><span>${fmt(d.health, 0)} / ${fmt(d.maxHealth, 0)}</span></div></div>`;

    html += `<div class='equip-stats' style='margin-top:8px'>`;
    html += `<div class='equip-stat'><span class='equip-stat-label'>ATK</span><span class='equip-stat-val'>${d.attack ?? '?'}</span></div>`;
    html += `<div class='equip-stat'><span class='equip-stat-label'>Ranged</span><span class='equip-stat-val'>${d.rangedAttack ?? '?'}</span></div>`;
    html += `<div class='equip-stat'><span class='equip-stat-label'>DEF</span><span class='equip-stat-val'>${d.defense ?? '?'}</span></div>`;
    html += `<div class='equip-stat'><span class='equip-stat-label'>Affinity</span><span class='equip-stat-val' style='color:${(d.critical || 0) > 0 ? '#3fb950' : (d.critical || 0) < 0 ? '#f85149' : '#c9d1d9'}'>${(d.critical || 0) > 0 ? '+' : ''}${d.critical ?? 0}%</span></div>`;
    if (d.element && d.element !== 'NONE') {
      const elemColor = elementColors[d.element] || '#8b949e';
      html += `<div class='equip-stat'><span class='equip-stat-label' style='color:${elemColor}'>${d.element}</span><span class='equip-stat-val' style='color:${elemColor}'>${d.elementValue ?? 0}</span></div>`;
    }
    html += `</div>`;

    el.innerHTML = html;
  } catch(e) {
    setDot('palico-card', false);
    document.getElementById('palico-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Monsters (MHWilds) ─────────────────────────────────────────

async function updateMonsters() {
  try {
    const d = await fetchJson('/api/monsters');
    const el = document.getElementById('monsters-content');
    setDot('monsters-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    document.getElementById('monsters-count').textContent = `(${d.count})`;

    if (!d.monsters || d.monsters.length === 0) {
      el.innerHTML = `<span class='error-msg'>No large monsters</span>`;
      return;
    }

    // Sort by distance (closest first), null distance last
    d.monsters.sort((a, b) => (a.distance ?? 9999) - (b.distance ?? 9999));

    el.innerHTML = d.monsters.map(m => {
      const pct = m.maxHealth > 0 ? m.health / m.maxHealth : 0;
      const color = hpColor(pct);
      const hpText = m.health != null ? `${fmt(m.health, 0)} / ${fmt(m.maxHealth, 0)}` : '?';
      const species = m.species ? `<span class='monster-species'>${esc(m.species)}</span>` : '';
      const dist = m.distance != null ? `<span class='monster-dist'>${fmt(m.distance, 0)}m</span>` : '';
      const pos = m.position && m.position.x != null
        ? `${fmt(m.position.x, 0)}, ${fmt(m.position.y, 0)}, ${fmt(m.position.z, 0)}`
        : '?';

      return `<div class='monster-row'>
        <div class='monster-header'>
          <span><span class='monster-name'>${esc(m.name)}</span>${species}</span>
          <span class='monster-hp-text'>${hpText}</span>
        </div>
        <div class='hp-bar-bg hp-bar-sm'>
          <div class='hp-bar' style='width:${(pct * 100).toFixed(0)}%;background:${color}'></div>
        </div>
        <div class='monster-meta'>
          <span>pos ${pos}</span>
          ${dist}
        </div>
      </div>`;
    }).join('');
  } catch(e) {
    setDot('monsters-card', false);
    document.getElementById('monsters-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Enemies (RE9) ───────────────────────────────────────────────────

async function updateEnemies() {
  try {
    const d = await fetchJson('/api/enemies');
    const el = document.getElementById('enemies-content');
    setDot('enemies-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    document.getElementById('enemies-count').textContent = `(${d.count})`;

    if (!d.enemies || d.enemies.length === 0) {
      el.innerHTML = d.isPlayerInSafeRoom
        ? `<span class='error-msg'>Safe room &mdash; no enemies</span>`
        : `<span class='error-msg'>No active enemies</span>`;
      return;
    }

    el.innerHTML = d.enemies.map(e => {
      const pct = e.maxHealth > 0 ? e.health / e.maxHealth : 0;
      const color = hpColor(pct);
      const dead = e.isDead ? " <span class='badge badge-dead'>Dead</span>" : '';
      const elite = e.isElite ? " <span class='badge badge-elite'>Elite</span>" : '';
      const hpText = e.health != null ? `${e.health} / ${e.maxHealth}` : '?';

      return `<div class='enemy-row'>
        <div class='enemy-header'>
          <span class='enemy-name'>${e.name || e.kindId || '?'}${elite}${dead}</span>
          <span class='enemy-hp-text'>${hpText}</span>
        </div>
        <div class='hp-bar-bg hp-bar-sm'>
          <div class='hp-bar' style='width:${(pct * 100).toFixed(0)}%;background:${color}'></div>
        </div>
      </div>`;
    }).join('');
  } catch(e) {
    setDot('enemies-card', false);
    document.getElementById('enemies-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Mr. X Tracker ───────────────────────────────────────────────────

async function updateStalker() {
  try {
    const d = await fetchJson('/api/stalker');
    const el = document.getElementById('stalker-content');
    if (d.error) { setDot('stalker-card', false); el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }
    setDot('stalker-card', true);

    if (!d.active) {
      el.innerHTML = `<span class='error-msg'>${d.reason || 'Not active'}</span>`;
      return;
    }

    const row = (l, v) => `<div class='stalker-row'><span class='label'>${l}</span><span class='value'>${v}</span></div>`;

    // Status badge color
    const statusColors = {
      'Stunned': '#3fb950', 'Searching': '#d29922', 'Chasing': '#f85149',
      'Chasing (Aggressive)': '#da3633', 'Teleporting': '#a371f7'
    };
    const statusColor = statusColors[d.status] || '#8b949e';
    const statusBadge = `<span class='stalker-status' style='background:${statusColor}'>${d.status}</span>`;

    // Distance indicator
    const distColor = d.playerDistance < 5 ? '#da3633' : d.playerDistance < 15 ? '#d29922' : '#3fb950';
    const distBar = d.playerDistance < 50 ? `<div class='stalker-dist-bar'><div class='stalker-dist-fill' style='width:${Math.min(100, (1 - d.playerDistance / 50) * 100).toFixed(0)}%;background:${distColor}'></div></div>` : '';

    let html = `<div class='stalker-header'>${statusBadge}<span class='stalker-state'>${d.state || ''}</span></div>`;
    html += row('Location', d.location || '?');
    html += row('Distance', `<span style='color:${distColor};font-weight:600'>${d.playerDistance}m</span>`);
    html += distBar;

    // AI flags
    const flags = [];
    if (d.ai.isScreenTarget) flags.push('<span class="badge" style="background:#da3633">On Screen</span>');
    if (d.ai.isDoorOpening) flags.push('<span class="badge" style="background:#d29922">Door</span>');
    if (d.ai.isTargetInSafeRoom) flags.push('<span class="badge" style="background:#3fb950">Safe Room</span>');
    if (d.ai.isGhostMode) flags.push('<span class="badge" style="background:#a371f7">Ghost</span>');
    if (d.ai.alwaysChaserMode) flags.push('<span class="badge" style="background:#8b949e">Persistent</span>');
    if (flags.length > 0) html += `<div class='stalker-flags'>${flags.join(' ')}</div>`;

    // Timers (only show non-zero ones)
    const timers = [];
    if (d.timers.killingTimer > 0) timers.push(`Kill: ${d.timers.killingTimer}s`);
    if (d.timers.sleepTimer > 0) timers.push(`Stun: ${d.timers.sleepTimer}s`);
    if (d.timers.inAttackSightTimer > 0) timers.push(`Sight: ${d.timers.inAttackSightTimer}s`);
    if (timers.length > 0) html += row('Timers', `<span class='stalker-timers'>${timers.join(' | ')}</span>`);

    el.innerHTML = html;
  } catch(e) {
    setDot('stalker-card', false);
    document.getElementById('stalker-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

// ── Game Info ────────────────────────────────────────────────────────

const weaponCategoryIcons = {
  Handgun: '🔫', Shotgun: '💥', Rifle: '🎯', MachineGun: '🔫',
  RocketLauncher: '🚀', Knife: '🔪', Axe: '🪓', Chainsaw: '⛓️',
  PoisonPump: '☠️', Throwing: '💣'
};

function formatDifficulty(id) {
  if (!id) return '?';
  // e.g. "ID0010" → "Easy", "ID0020" → "Normal", "ID0030" → "Hard"
  const map = { ID0010: 'Assisted', ID0020: 'Standard', ID0030: 'Hardcore' };
  return map[id] || id;
}

async function updateGameInfo() {
  try {
    const d = await fetchJson('/api/gameinfo');
    const el = document.getElementById('gameinfo-content');
    setDot('gameinfo-card', !d.error);
    if (d.error) { el.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    // Detect RE2 by scenarioType field
    if (d.scenarioType !== undefined) {
      renderGameInfoRE2(d, el);
    } else {
      renderGameInfoDefault(d, el);
    }
  } catch(e) {
    setDot('gameinfo-card', false);
    document.getElementById('gameinfo-content').innerHTML = `<span class='error-msg'>${e.message}</span>`;
  }
}

function renderGameInfoRE2(d, el) {
  const clean = s => s ? s.split(' ')[0] : '?';
  let html = '';

  // Scenario + Difficulty
  html += row('Scenario', clean(d.scenarioType), 'highlight');
  if (d.difficulty) html += row('Difficulty', clean(d.difficulty));
  if (d.mainState) html += row('State', clean(d.mainState));

  // Status badges
  const badges = [];
  if (d.isInGame) badges.push("<span class='badge badge-safe'>In Game</span>");
  if (d.isInPause) badges.push("<span class='badge badge-elite'>Paused</span>");
  if (d.isInTitle) badges.push("<span class='badge badge-dead'>Title</span>");
  if (d.is2ndStory) badges.push("<span class='badge badge-elite'>2nd Run</span>");
  if (d.isExtraGame) badges.push("<span class='badge badge-elite'>Extra</span>");
  if (badges.length) html += `<div class='status-badges'>${badges.join(' ')}</div>`;

  // Location
  if (d.locationName || d.location) {
    html += `<div style='margin-top:8px;padding-top:8px;border-top:1px solid #21262d'>`;
    html += row('Location', d.locationName || clean(d.location));
    if (d.currentArea) html += row('Area', clean(d.currentArea));
    if (d.currentMap) html += row('Map', clean(d.currentMap));
    if (d.playerInSafeRoom) html += row('Safe Room', 'Yes', 'highlight');
    const tyrant = d.tyrantMap ? clean(d.tyrantMap) : null;
    if (tyrant && tyrant !== 'Invalid' && tyrant !== '0') html += row('Mr. X', tyrant, 'warn');
    html += `</div>`;
  }

  // Game Rank (adaptive difficulty)
  if (d.gameRank) {
    const gr = d.gameRank;
    html += `<div style='margin-top:8px;padding-top:8px;border-top:1px solid #21262d'>`;
    html += row('Adaptive Rank', gr.rank != null ? gr.rank : '?');
    if (gr.rankPoint != null) html += row('Rank Points', fmt(gr.rankPoint, 0));
    html += `<div class='equip-stats' style='margin-top:6px'>`;
    if (gr.enemyDamageRate != null) html += `<div class='equip-stat'><span class='equip-stat-label'>Enemy DMG</span><span class='equip-stat-val'>${fmt(gr.enemyDamageRate, 2)}x</span></div>`;
    if (gr.playerDamageRate != null) html += `<div class='equip-stat'><span class='equip-stat-label'>Player DMG</span><span class='equip-stat-val'>${fmt(gr.playerDamageRate, 2)}x</span></div>`;
    if (gr.enemyBreakRate != null) html += `<div class='equip-stat'><span class='equip-stat-label'>Break Rate</span><span class='equip-stat-val'>${fmt(gr.enemyBreakRate, 2)}x</span></div>`;
    html += `</div></div>`;
  }

  // Stats
  html += `<div style='margin-top:8px;padding-top:8px;border-top:1px solid #21262d'>`;
  if (d.saveTimes != null) html += row('Saves', d.saveTimes);
  if (d.totalEnemyKills != null) html += row('Kills', d.totalEnemyKills);
  html += `</div>`;

  el.innerHTML = html;
}

function renderGameInfoDefault(d, el) {
  let html = '';

  // Chapter / Progress
  if (d.chapter) {
    html += row('Chapter', d.chapter, 'highlight');
  }
  if (d.progressNo != null) {
    html += row('Progress #', d.progressNo);
  }

  // Difficulty
  if (d.difficulty) {
    html += row('Difficulty', formatDifficulty(d.difficulty));
  }

  // Adaptive Rank
  if (d.rank != null) {
    const rankPct = d.rankMax > 0 ? (d.rank / d.rankMax * 100).toFixed(0) : 0;
    html += `<div style='margin-top:8px;padding-top:8px;border-top:1px solid #21262d'>`;
    html += row('Adaptive Rank', `${d.rank} / ${d.rankMax}`);
    html += `<div class='hp-bar-bg'><div class='hp-bar' style='width:${rankPct}%;background:#a371f7'></div></div>`;
    if (d.enemyDamageFactor != null) {
      html += `<div class='equip-stats' style='margin-top:6px'>`;
      html += `<div class='equip-stat'><span class='equip-stat-label'>Enemy DMG</span><span class='equip-stat-val'>${fmt(d.enemyDamageFactor, 2)}x</span></div>`;
      html += `<div class='equip-stat'><span class='equip-stat-label'>Player DMG</span><span class='equip-stat-val'>${fmt(d.playerDamageFactor, 2)}x</span></div>`;
      html += `<div class='equip-stat'><span class='equip-stat-label'>Enemy Move</span><span class='equip-stat-val'>${fmt(d.enemyMoveFactor, 2)}x</span></div>`;
      html += `<div class='equip-stat'><span class='equip-stat-label'>Wince</span><span class='equip-stat-val'>${fmt(d.enemyWinceFactor, 2)}x</span></div>`;
      html += `</div>`;
    }
    html += `</div>`;
  }

  // Play time
  if (d.playTimeSeconds != null) {
    html += row('Play Time', formatPlayTime(d.playTimeSeconds));
  }

  // Scene info
  if (d.isMainGame != null) {
    html += row('In Main Game', d.isMainGame ? 'Yes' : 'No');
  }

  // Clear data
  if (d.newGameStarts != null || d.totalClears != null) {
    html += `<div style='margin-top:8px;padding-top:8px;border-top:1px solid #21262d'>`;
    if (d.totalClears != null) html += row('Clears', d.totalClears);
    if (d.newGameStarts != null) html += row('New Games', d.newGameStarts);
    html += `</div>`;
  }

  // Collectibles
  if (d.collectibles) {
    const c = d.collectibles;
    html += `<div style='margin-top:8px;padding-top:8px;border-top:1px solid #21262d'>`;
    html += `<div style='color:#8b949e;font-size:0.8em;margin-bottom:6px'>Collectibles</div>`;
    for (const [label, data] of [['Safes', c.safes], ['Containers', c.containers], ['Fragile Symbols', c.fragileSymbols]]) {
      if (!data) continue;
      const pct = data.max > 0 ? (data.found / data.max * 100).toFixed(0) : 0;
      const done = data.found >= data.max;
      html += `<div style='margin-bottom:4px'>
        <div style='display:flex;justify-content:space-between;font-size:0.85em'>
          <span>${label}</span>
          <span style='color:${done ? '#3fb950' : '#c9d1d9'}'>${data.found} / ${data.max}</span>
        </div>
        <div class='hp-bar-bg hp-bar-sm'><div class='hp-bar' style='width:${pct}%;background:${done ? '#3fb950' : '#58a6ff'}'></div></div>
      </div>`;
    }
    html += `</div>`;
  }

  // Weapon & Combat state
  if (d.weapon || d.combat) {
    html += `<div style='margin-top:8px;padding-top:8px;border-top:1px solid #21262d'>`;
    if (d.weapon) {
      const wName = d.weaponName && d.weaponName !== d.weapon ? `${d.weaponName} (${d.weapon})` : d.weapon;
      html += row('Weapon', wName, 'highlight');
    }
    if (d.fearLevel != null) {
      const fearPct = (d.fearLevel * 100).toFixed(0);
      const fearColor = d.fearLevel > 0.6 ? '#f85149' : d.fearLevel > 0.3 ? '#d29922' : '#3fb950';
      html += `<div style='margin-top:4px'>
        <div style='display:flex;justify-content:space-between;font-size:0.85em'>
          <span>Fear Level</span>
          <span style='color:${fearColor}'>${fearPct}%</span>
        </div>
        <div class='hp-bar-bg hp-bar-sm'><div class='hp-bar' style='width:${fearPct}%;background:${fearColor}'></div></div>
      </div>`;
    }
    if (d.combat) {
      const badges = [];
      if (d.combat.isHolding) badges.push('Holding');
      if (d.combat.isShooting) badges.push('Shooting');
      if (d.combat.isReloading) badges.push('Reloading');
      if (d.combat.isMeleeAttack) badges.push('Melee');
      if (d.combat.isCrouch) badges.push('Crouching');
      if (d.combat.isRun) badges.push('Running');
      if (d.combat.isIdle) badges.push('Idle');
      if (badges.length > 0) {
        html += `<div class='status-badges' style='margin-top:6px'>${badges.map(b => `<span class='badge badge-safe'>${b}</span>`).join(' ')}</div>`;
      }
    }
    html += `</div>`;
  }

  // Scenario time
  if (d.scenarioTime) {
    html += row('Scenario', d.scenarioTime);
  }

  el.innerHTML = html;
}

// ── Chat ────────────────────────────────────────────────────────────

let lastChatCount = -1;

async function updateChat() {
  try {
    const d = await fetchJson('/api/chat');
    const log = document.getElementById('chat-log');
    setDot('chat-card', !d.error);
    if (d.error) { log.innerHTML = `<span class='error-msg'>${d.error}</span>`; return; }

    document.getElementById('chat-count').textContent = `(${d.count})`;

    // Only rebuild if count changed
    if (d.count === lastChatCount) return;
    lastChatCount = d.count;

    if (!d.messages || d.messages.length === 0) {
      log.innerHTML = `<span class='error-msg'>No messages</span>`;
      return;
    }

    log.innerHTML = d.messages.map(m => {
      const time = m.time ? `<span class='chat-time'>${m.time}</span>` : '';
      return `<div class='chat-msg'>
        ${time}<span class='chat-sender'>${esc(m.sender)}</span>
        <span class='chat-text'>${esc(m.text)}</span>
      </div>`;
    }).join('');

    // Auto-scroll to bottom
    log.scrollTop = log.scrollHeight;
  } catch(e) {
    setDot('chat-card', false);
  }
}

(function() {
  const form = document.getElementById('chat-form');
  const input = document.getElementById('chat-input');
  const status = document.getElementById('chat-status');

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const msg = input.value.trim();
    if (!msg) return;

    const btn = form.querySelector('.chat-send');
    btn.disabled = true;
    status.textContent = '';

    try {
      const resp = await fetch(API_BASE + '/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: msg })
      });
      const d = await resp.json();

      if (d.ok) {
        status.textContent = 'Sent!';
        status.className = 'chat-status chat-ok';
        input.value = '';
        lastChatCount = -1; // force refresh
        updateChat();
      } else {
        status.textContent = d.error || 'Failed';
        status.className = 'chat-status chat-err';
      }
    } catch(err) {
      status.textContent = err.message;
      status.className = 'chat-status chat-err';
    }

    btn.disabled = false;
    setTimeout(() => { status.textContent = ''; }, 3000);
  });
})();

// ── Game name mapping ──────────────────────────────────────────────
const GAME_NAMES = {
  MonsterHunterWilds: 'Monster Hunter Wilds',
  DevilMayCry5: 'Devil May Cry 5',
  re2: 'Resident Evil 2',
  re3: 'Resident Evil 3',
  re4: 'Resident Evil 4',
  re8: 'Resident Evil Village',
  re9: 'Resident Evil 9',
  StreetFighter6: 'Street Fighter 6',
  DragonsDogma2: "Dragon's Dogma 2",
};

let gameEndpoints = [];

async function initDashboard() {
  try {
    const info = await fetchJson('/api');
    const game = info.game || 'REFramework';
    const title = GAME_NAMES[game] || game;
    document.getElementById('game-title').textContent = title;
    document.title = title + ' Dashboard';
    gameEndpoints = info.endpoints || [];
  } catch {
    document.getElementById('game-title').textContent = 'REFramework (offline)';
  }

  const has = (ep) => gameEndpoints.includes(ep);

  // Hide cards for endpoints not available in this game
  const cardMap = {
    '/api/player': 'player-card',
    '/api/weather': 'weather-card',
    '/api/map': 'map-card',
    '/api/equipment': 'equip-card',
    '/api/palico': 'palico-card',
    '/api/huntlog': 'huntlog-card',
    '/api/inventory': 'inventory-card',
    '/api/meshes': 'mesh-card',
    '/api/chat': 'chat-card',
    '/api/lobby': 'lobby-card',
    '/api/enemies': 'enemies-card',
    '/api/gameinfo': 'gameinfo-card',
    '/api/monsters': 'monsters-card',
    '/api/stalker': 'stalker-card',
  };
  for (const [ep, cardId] of Object.entries(cardMap)) {
    if (!has(ep)) {
      const el = document.getElementById(cardId);
      if (el) el.style.display = 'none';
    }
  }

  // Fast-polling data (camera always, player if available) every 500ms
  setInterval(poll, 500);
  poll();

  // Medium-polling: only start polls for endpoints the game exposes
  if (has('/api/lobby'))     { updateLobby();     setInterval(updateLobby, 5000); }
  if (has('/api/weather'))   { updateWeather();   setInterval(updateWeather, 5000); }
  if (has('/api/equipment')) { updateEquipment(); setInterval(updateEquipment, 5000); }
  if (has('/api/inventory')) { updateInventory(); setInterval(updateInventory, 5000); }
  if (has('/api/meshes'))    { updateMeshes();    setInterval(updateMeshes, 5000); }
  if (has('/api/map'))       { updateMap();       setInterval(updateMap, 5000); }
  if (has('/api/chat'))      { updateChat();      setInterval(updateChat, 3000); }
  if (has('/api/huntlog'))   { updateHuntLog();   setInterval(updateHuntLog, 10000); }
  if (has('/api/monsters'))  { updateMonsters();  setInterval(updateMonsters, 2000); }
  if (has('/api/palico'))    { updatePalico();    setInterval(updatePalico, 5000); }
  if (has('/api/enemies'))   { updateEnemies();   setInterval(updateEnemies, 1000); }
  if (has('/api/stalker'))   { updateStalker();   setInterval(updateStalker, 1000); }
  if (has('/api/gameinfo'))  { updateGameInfo();  setInterval(updateGameInfo, 3000); }
}

async function poll() {
  pollCount++;
  const start = performance.now();
  const tasks = [updateCamera(), updateFPSFast()];
  if (gameEndpoints.includes('/api/player')) tasks.push(updatePlayer());
  await Promise.all(tasks);
  const ms = (performance.now() - start).toFixed(0);
  document.getElementById('poll-info').textContent = `Poll #${pollCount} | ${ms}ms | ${new Date().toLocaleTimeString()}`;
}

initDashboard();

// ── Tooltip for [data-tip] elements ──────────────────────────────
(function() {
  const tip = document.createElement('div');
  tip.className = 'tip';
  document.body.appendChild(tip);

  document.addEventListener('mouseover', e => {
    const el = e.target.closest('[data-tip]');
    if (!el) { tip.classList.remove('show'); return; }
    tip.textContent = el.dataset.tip;
    tip.classList.add('show');
    positionTip(e);
  });

  document.addEventListener('mousemove', e => {
    if (tip.classList.contains('show')) positionTip(e);
  });

  document.addEventListener('mouseout', e => {
    const el = e.target.closest('[data-tip]');
    if (el && !el.contains(e.relatedTarget)) tip.classList.remove('show');
  });

  function positionTip(e) {
    const pad = 12;
    let x = e.clientX + pad, y = e.clientY + pad;
    tip.style.left = '0'; tip.style.top = '0';
    const r = tip.getBoundingClientRect();
    if (x + r.width > window.innerWidth) x = e.clientX - r.width - pad;
    if (y + r.height > window.innerHeight) y = e.clientY - r.height - pad;
    tip.style.left = x + 'px';
    tip.style.top = y + 'px';
  }
})();
