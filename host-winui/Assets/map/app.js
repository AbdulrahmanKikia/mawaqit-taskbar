/* global L */
(function () {
  const statusEl = document.getElementById('status');
  const listEl = document.getElementById('list');
  const useBtn = document.getElementById('useBtn');
  const favBtn = document.getElementById('favBtn');
  const pickedLabel = document.getElementById('pickedLabel');
  const qEl = document.getElementById('q');
  let mosques = [];
  let selected = null;
  let markers = [];
  let moveTimer = null;
  let suppressMove = false;
  let map = null;
  let i18n = {};
  // Replies to older nearby/search requests are ignored so fast panning cannot show stale results.
  let seq = 0;
  let mode = 'mosque';
  let pendingView = null;
  let loadingMore = false;

  function t(key, fallback) {
    return i18n[key] || fallback || key;
  }

  function fmt(key, fallback, value) {
    return String(t(key, fallback)).replace('{0}', value);
  }

  function applyChromeStrings() {
    const title = t('Map.Title', 'Find your mosque');
    document.title = title;
    const h1 = document.getElementById('mapTitle');
    const hint = document.getElementById('mapHint');
    if (h1) h1.textContent = title;
    if (hint) hint.textContent = t('Map.Hint', 'Tap a name on the map or list, then Use selected or Set as favorite.');
    const searchBtn = document.getElementById('searchBtn');
    const locBtn = document.getElementById('locBtn');
    const refreshBtn = document.getElementById('refreshBtn');
    if (searchBtn) searchBtn.textContent = t('Map.Filter', 'Filter');
    if (qEl) qEl.placeholder = t('Map.Placeholder', 'Filter by name or city…');
    if (locBtn) locBtn.textContent = t('Map.MyLocation', 'My location');
    if (refreshBtn) refreshBtn.textContent = t('Map.Refresh', 'Refresh map');
    if (useBtn) useBtn.textContent = t('Map.Use', 'Use selected');
    if (favBtn) favBtn.textContent = t('Map.Favorite', 'Set as favorite');
    setText('legendMawaqit', t('Map.LegendMawaqit', 'Mosque timetable'));
    setText('legendOsm', t('Map.LegendOsm', 'Calculated times'));
    setText('spotBtn', t('Map.UsePin', 'Use the pin as my location'));
    if (mode === 'location') {
      document.title = t('Map.LocationTitle', 'Set your location');
      setText('mapTitle', t('Map.LocationTitle', 'Set your location'));
      setText('mapHint', t('Map.LocationHint', 'Move the map until the pin is where you pray.'));
    }
    paintPicked();
  }

  function setText(id, text) {
    const el = document.getElementById(id);
    if (el) el.textContent = text;
  }

  function isOsm(m) {
    return m && m.provider === 'osm';
  }

  function paintPicked() {
    const note = document.getElementById('pickedNote');
    if (note) {
      note.textContent = isOsm(selected)
        ? t('Map.OsmNote', 'Not on Mawaqit: adhan times are calculated for this mosque, and you can add its iqama in Preferences.')
        : '';
    }
    if (mode === 'location') {
      pickedLabel.textContent = t('Map.LocationHint', 'Move the map until the pin is where you pray.');
      return;
    }
    if (selected) {
      const strong = document.createElement('strong');
      strong.textContent = displayName(selected);
      pickedLabel.replaceChildren(t('Map.SelectedPrefix', 'Selected:') + ' ', strong);
    } else {
      pickedLabel.textContent = t('Map.None', 'No mosque selected');
    }
  }

  window.__setLang = function (pack) {
    i18n = pack || {};
    const root = document.documentElement;
    root.lang = i18n.lang || 'en';
    root.dir = i18n.rtl === '1' ? 'rtl' : 'ltr';
    applyChromeStrings();
    if (mosques.length) {
      setStatus(fmt('Map.Count', '{0} mosque name(s)', mosques.length));
    }
    if (map) {
      try {
        map.invalidateSize();
      } catch (_) {}
    }
  };

  function applyIpcResult(res) {
    if (!res) return;
    if (res.mosques) {
      window.__setMosques(res.mosques, res.message || '', !!res.fit);
    } else if (res.error) {
      window.__setError(res.error);
    } else if (res.ok === 'selected') {
      const p = document.createElement('p');
      p.style.cssText = 'padding:24px;font:16px/1.4 system-ui';
      p.textContent = 'Mosque saved. You can close this tab.';
      document.body.replaceChildren(p);
    }
  }

  function ipc(payload) {
    const data = JSON.stringify(payload);
    if (window.ipc && typeof window.ipc.postMessage === 'function') {
      window.ipc.postMessage(data);
      return;
    }
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(data);
      return;
    }
    fetch('/ipc', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: data,
    })
      .then((r) => r.json())
      .then(applyIpcResult)
      .catch((e) => setStatus(String(e)));
  }

  function setStatus(text) {
    statusEl.textContent = text;
  }

  function displayName(m) {
    return (m.label || m.slug || '').split(' — ')[0];
  }

  function clearMarkers() {
    markers.forEach((m) => map.removeLayer(m));
    markers = [];
  }

  function setActionsEnabled(on) {
    useBtn.disabled = !on;
    if (favBtn) favBtn.disabled = !on;
  }

  function confirmSelection(favorite) {
    if (!selected) return;
    ipc({
      type: 'select',
      slug: selected.slug,
      label: selected.label,
      favorite: !!favorite,
      lat: selected.lat,
      lon: selected.lon,
      provider: selected.provider || 'mawaqit',
      website: selected.website || undefined,
    });
  }

  function selectMosque(m, pan) {
    selected = m;
    setActionsEnabled(!!m);
    paintPicked();
    [...listEl.children].forEach((el) => {
      el.classList.toggle('active', m && el.dataset.slug === m.slug);
    });
    markers.forEach((layer) => {
      const tip = layer.getTooltip && layer.getTooltip();
      if (!tip) return;
      const el = tip.getElement && tip.getElement();
      if (!el) return;
      const tag = el.querySelector('.name-tag');
      if (tag) tag.classList.toggle('active', m && layer._mosqueSlug === m.slug);
    });
    if (pan && m && m.lat != null && m.lon != null) {
      suppressMove = true;
      map.panTo([m.lat, m.lon]);
      setTimeout(() => {
        suppressMove = false;
      }, 400);
    }
  }

  function renderList() {
    listEl.innerHTML = '';
    mosques.forEach((m) => {
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'item' + (selected && selected.slug === m.slug ? ' active' : '');
      btn.dataset.slug = m.slug;
      btn.innerHTML = '<div class="name"></div><span class="badge"></span><div class="meta"></div>';
      btn.querySelector('.name').textContent = displayName(m);
      const badge = btn.querySelector('.badge');
      badge.classList.add(isOsm(m) ? 'osm' : 'mawaqit');
      badge.textContent = isOsm(m) ? t('Map.BadgeOsm', 'Calculated') : t('Map.BadgeMawaqit', 'Timetable');
      btn.querySelector('.meta').textContent = m.label.includes(' — ')
        ? m.label.split(' — ').slice(1).join(' — ')
        : m.slug || '';
      btn.onclick = () => selectMosque(m, true);
      btn.ondblclick = () => {
        selectMosque(m, false);
        confirmSelection(false);
      };
      listEl.appendChild(btn);
    });
  }

  function chromeColors() {
    const s = getComputedStyle(document.documentElement);
    return {
      primary: (s.getPropertyValue('--primary') || '#1f5fd6').trim(),
      highlight: (s.getPropertyValue('--highlight') || '#2f74f0').trim(),
      panel: (s.getPropertyValue('--panel') || '#ffffff').trim(),
    };
  }

  function renderMarkers() {
    clearMarkers();
    if (!map) return;
    const paint = chromeColors();
    const withCoords = mosques.filter((m) => m.lat != null && m.lon != null);
    withCoords.forEach((m) => {
      const name = displayName(m);
      const osm = isOsm(m);
      const marker = L.circleMarker([m.lat, m.lon], {
        radius: osm ? 5 : 6,
        color: osm ? paint.highlight : paint.panel,
        weight: 2,
        fillColor: osm ? paint.panel : paint.primary,
        fillOpacity: 1,
      }).addTo(map);
      marker._mosqueSlug = m.slug;
      const tag = document.createElement('span');
      tag.className = 'name-tag' + (osm ? ' osm' : '');
      tag.textContent = name;
      marker.bindTooltip(tag, {
        permanent: true,
        direction: 'top',
        offset: [0, -6],
        className: 'name-tip',
        opacity: 1,
      });
      marker.on('click', () => selectMosque(m, false));
      marker.on('dblclick', () => {
        selectMosque(m, false);
        confirmSelection(false);
      });
      markers.push(marker);
    });
  }

  window.__setMosques = function (list, message, fit, replySeq, moreComing) {
    if (replySeq && replySeq !== seq) return;
    loadingMore = !!moreComing;
    const keepSlug = selected && selected.slug;
    mosques = Array.isArray(list) ? list : [];
    selected = keepSlug ? mosques.find((m) => m.slug === keepSlug) || null : null;
    setActionsEnabled(!!selected);
    paintPicked();
    showCount(message);
    renderList();
    renderMarkers();
    if (fit && map) {
      const withCoords = mosques.filter((m) => m.lat != null && m.lon != null);
      if (withCoords.length) {
        const b = L.latLngBounds(withCoords.map((m) => [m.lat, m.lon]));
        if (b.isValid()) {
          suppressMove = true;
          map.fitBounds(b, { padding: [28, 28], maxZoom: 13 });
          setTimeout(() => {
            suppressMove = false;
          }, 500);
        }
      }
    }
  };

  function showCount(message) {
    let text = message ||
      (mosques.length ? fmt('Map.Count', '{0} mosque name(s)', mosques.length) : t('Map.NoneInView', 'No mosques in view'));
    if (loadingMore) text += ' · ' + t('Map.LoadingMore', 'looking for more…');
    setStatus(text);
  }

  // OpenStreetMap mosques for the same request, added after the Mawaqit list is on screen.
  window.__addMosques = function (list, replySeq) {
    if (replySeq && replySeq !== seq) return;
    loadingMore = false;
    const known = new Set(mosques.map((m) => m.slug));
    (Array.isArray(list) ? list : []).forEach((m) => {
      if (!known.has(m.slug)) mosques.push(m);
    });
    showCount('');
    renderList();
    renderMarkers();
  };

  window.__setError = function (message, replySeq) {
    if (replySeq && replySeq !== seq) return;
    setStatus(message || t('Map.Error', 'Something went wrong'));
  };

  // The map keeps the app's blue palette; only light/dark follows Windows.
  window.__setChrome = function (_accent, dark) {
    const root = document.documentElement;
    root.classList.toggle('theme-dark', !!dark);
    root.classList.toggle('theme-light', !dark);
    if (map) renderMarkers();
  };

  window.__setMode = function (next, lat, lon) {
    mode = next === 'location' ? 'location' : 'mosque';
    document.body.classList.toggle('mode-location', mode === 'location');
    if (typeof lat === 'number' && typeof lon === 'number') {
      if (map) {
        suppressMove = true;
        map.setView([lat, lon], mode === 'location' ? 12 : 13);
        setTimeout(() => {
          suppressMove = false;
          if (mode === 'mosque') requestNearby();
        }, 300);
      } else {
        pendingView = [lat, lon];
      }
    }
    applyChromeStrings();
  };

  function requestNearby() {
    if (!map || mode === 'location') return;
    const b = map.getBounds();
    const c = map.getCenter();
    setStatus(t('Map.Loading', 'Loading mosque names…'));
    ipc({
      type: 'nearby',
      seq: ++seq,
      lat: c.lat,
      lon: c.lng,
      north: b.getNorth(),
      south: b.getSouth(),
      east: b.getEast(),
      west: b.getWest(),
    });
  }

  function requestSearch() {
    const word = qEl.value.trim();
    if (!word) {
      requestNearby();
      return;
    }
    setStatus(fmt('Map.Matching', 'Matching “{0}” on Mawaqit…', word));
    ipc({ type: 'search', word: word, seq: ++seq });
  }

  function bootMap() {
    setStatus(t('Map.MapReady', 'Map ready — loading mosque names…'));
    map = L.map('map', { zoomControl: true }).setView(pendingView || [45.5017, -73.5673], 12);
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; OpenStreetMap',
    }).addTo(map);

    map.on('moveend', () => {
      if (suppressMove) return;
      clearTimeout(moveTimer);
      moveTimer = setTimeout(requestNearby, 400);
    });

    document.getElementById('searchBtn').onclick = requestSearch;
    document.getElementById('refreshBtn').onclick = () => {
      qEl.value = '';
      requestNearby();
    };
    qEl.addEventListener('keydown', (e) => {
      if (e.key === 'Enter') requestSearch();
    });
    document.getElementById('locBtn').onclick = () => {
      setStatus(t('Map.Locating', 'Locating…'));
      if (!navigator.geolocation) {
        setStatus(t('Map.GeoUnavailable', 'Geolocation unavailable — pan the map instead.'));
        requestNearby();
        return;
      }
      navigator.geolocation.getCurrentPosition(
        (pos) => {
          suppressMove = true;
          map.setView([pos.coords.latitude, pos.coords.longitude], 13);
          setTimeout(() => {
            suppressMove = false;
            requestNearby();
          }, 300);
        },
        () => {
          setStatus(t('Map.LocationDenied', 'Location denied — pan the map or filter by name.'));
          requestNearby();
        },
        { enableHighAccuracy: false, timeout: 8000 }
      );
    };
    useBtn.onclick = () => confirmSelection(false);
    document.getElementById('spotBtn').onclick = () => {
      const c = map.getCenter();
      ipc({ type: 'location', lat: c.lat, lon: c.lng, label: '' });
    };
    if (favBtn) favBtn.onclick = () => confirmSelection(true);

    ipc({ type: 'ready' });
    setTimeout(() => {
      try {
        map.invalidateSize();
      } catch (_) {}
      requestNearby();
    }, 350);
  }

  setStatus(t('Map.LoadingLib', 'Loading map library…'));
  const s = document.createElement('script');
  s.src = '/leaflet.js';
  s.onload = function () {
    if (typeof L === 'undefined') {
      setStatus(t('Map.LeafletMissing', 'Map library loaded but Leaflet is missing.'));
      return;
    }
    bootMap();
  };
  s.onerror = function () {
    setStatus(t('Map.CouldNotLoad', 'Could not load local map library.'));
  };
  document.head.appendChild(s);
})();
