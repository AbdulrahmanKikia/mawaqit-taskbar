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
    if (hint) hint.textContent = t('Map.Hint', 'Click a name on the map or list. Double-click to use it.');
    const searchBtn = document.getElementById('searchBtn');
    const locBtn = document.getElementById('locBtn');
    const refreshBtn = document.getElementById('refreshBtn');
    if (searchBtn) searchBtn.textContent = t('Map.Filter', 'Filter');
    if (qEl) qEl.placeholder = t('Map.Placeholder', 'Filter by name or city…');
    if (locBtn) locBtn.textContent = t('Map.MyLocation', 'My location');
    if (refreshBtn) refreshBtn.textContent = t('Map.Refresh', 'Refresh map');
    if (useBtn) useBtn.textContent = t('Map.Use', 'Use selected');
    if (favBtn) favBtn.textContent = t('Map.Favorite', 'Set as favorite');
    paintPicked();
  }

  function paintPicked() {
    if (selected) {
      pickedLabel.innerHTML = t('Map.SelectedPrefix', 'Selected:') + ' <strong></strong>';
      pickedLabel.querySelector('strong').textContent = displayName(selected);
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
      document.body.innerHTML =
        '<p style="padding:24px;font:16px/1.4 system-ui">Mosque saved. You can close this tab.</p>';
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

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
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
      btn.innerHTML = '<div class="name"></div><div class="meta"></div>';
      btn.querySelector('.name').textContent = displayName(m);
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
      fill: (s.getPropertyValue('--accent') || '#0078d4').trim(),
      stroke: (s.getPropertyValue('--accent-text') || '#ffffff').trim(),
    };
  }

  function renderMarkers() {
    clearMarkers();
    if (!map) return;
    const paint = chromeColors();
    const withCoords = mosques.filter((m) => m.lat != null && m.lon != null);
    withCoords.forEach((m) => {
      const name = displayName(m);
      const marker = L.circleMarker([m.lat, m.lon], {
        radius: 5,
        color: paint.stroke,
        weight: 1,
        fillColor: paint.fill,
        fillOpacity: 0.95,
      }).addTo(map);
      marker._mosqueSlug = m.slug;
      marker.bindTooltip('<span class="name-tag">' + escapeHtml(name) + '</span>', {
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

  window.__setMosques = function (list, message, fit) {
    const keepSlug = selected && selected.slug;
    mosques = Array.isArray(list) ? list : [];
    selected = keepSlug ? mosques.find((m) => m.slug === keepSlug) || null : null;
    setActionsEnabled(!!selected);
    paintPicked();
    setStatus(
      message ||
        (mosques.length
          ? fmt('Map.Count', '{0} mosque name(s)', mosques.length)
          : t('Map.NoneInView', 'No mosques in view'))
    );
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

  window.__setError = function (message) {
    setStatus(message || t('Map.Error', 'Something went wrong'));
  };

  window.__setChrome = function (accent, dark, accentText) {
    const root = document.documentElement;
    if (accent) root.style.setProperty('--accent', accent);
    if (accentText) root.style.setProperty('--accent-text', accentText);
    root.classList.toggle('theme-dark', !!dark);
    root.classList.toggle('theme-light', !dark);
    if (map) renderMarkers();
  };

  function requestNearby() {
    if (!map) return;
    const b = map.getBounds();
    const c = map.getCenter();
    setStatus(t('Map.Loading', 'Loading mosque names…'));
    ipc({
      type: 'nearby',
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
    ipc({ type: 'search', word: word });
  }

  function bootMap() {
    setStatus(t('Map.MapReady', 'Map ready — loading mosque names…'));
    map = L.map('map', { zoomControl: true }).setView([45.5017, -73.5673], 12);
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
