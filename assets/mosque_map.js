/* global L */
(function () {
  const statusEl = document.getElementById('status');
  const listEl = document.getElementById('list');
  const useBtn = document.getElementById('useBtn');
  const pickedLabel = document.getElementById('pickedLabel');
  const qEl = document.getElementById('q');
  let mosques = [];
  let selected = null;
  let markers = [];
  let moveTimer = null;
  let suppressMove = false;
  let map = null;

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

  function confirmSelection() {
    if (!selected) return;
    ipc({ type: 'select', slug: selected.slug, label: selected.label });
  }

  function selectMosque(m, pan) {
    selected = m;
    useBtn.disabled = !m;
    if (m) {
      pickedLabel.innerHTML = 'Selected: <strong></strong>';
      pickedLabel.querySelector('strong').textContent = displayName(m);
    } else {
      pickedLabel.textContent = 'No mosque selected';
    }
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
        confirmSelection();
      };
      listEl.appendChild(btn);
    });
  }

  function renderMarkers() {
    clearMarkers();
    const withCoords = mosques.filter((m) => m.lat != null && m.lon != null);
    withCoords.forEach((m) => {
      const name = displayName(m);
      const marker = L.circleMarker([m.lat, m.lon], {
        radius: 5,
        color: '#1a1200',
        weight: 1,
        fillColor: '#ffcd60',
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
        confirmSelection();
      });
      markers.push(marker);
    });
  }

  window.__setMosques = function (list, message, fit) {
    const keepSlug = selected && selected.slug;
    mosques = Array.isArray(list) ? list : [];
    selected = keepSlug ? mosques.find((m) => m.slug === keepSlug) || null : null;
    useBtn.disabled = !selected;
    if (selected) {
      pickedLabel.innerHTML = 'Selected: <strong></strong>';
      pickedLabel.querySelector('strong').textContent = displayName(selected);
    } else {
      pickedLabel.textContent = 'No mosque selected';
    }
    setStatus(
      message || (mosques.length ? mosques.length + ' mosque name(s)' : 'No mosques in view')
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
    setStatus(message || 'Something went wrong');
  };

  function requestNearby() {
    if (!map) return;
    const b = map.getBounds();
    const c = map.getCenter();
    setStatus('Loading mosque names…');
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
    setStatus('Matching “' + word + '” on Mawaqit…');
    ipc({ type: 'search', word: word });
  }

  function bootMap() {
    setStatus('Map ready — loading mosque names…');
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
      setStatus('Locating…');
      if (!navigator.geolocation) {
        setStatus('Geolocation unavailable — pan the map instead.');
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
          setStatus('Location denied — pan the map or filter by name.');
          requestNearby();
        },
        { enableHighAccuracy: false, timeout: 8000 }
      );
    };
    useBtn.onclick = confirmSelection;

    ipc({ type: 'ready' });
    setTimeout(() => {
      try {
        map.invalidateSize();
      } catch (_) {}
      requestNearby();
    }, 350);
  }

  setStatus('Loading map library…');
  const s = document.createElement('script');
  s.src = '/leaflet.js';
  s.onload = function () {
    if (typeof L === 'undefined') {
      setStatus('Map library loaded but Leaflet is missing.');
      return;
    }
    bootMap();
  };
  s.onerror = function () {
    setStatus('Could not load local map library.');
  };
  document.head.appendChild(s);
})();
