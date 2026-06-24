/* Wikipedia Episode Order - Preview Page */
(function () {
    'use strict';

    var API_BASE = '/WikipediaOrder';

    function escapeHtml(str) {
        return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function formatDate(isoString) {
        if (!isoString) return '—';
        try {
            return new Date(isoString).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
        } catch (e) { return isoString; }
    }

    function getParams() {
        var hash = window.location.hash;
        var qi = hash.indexOf('?');
        var searchStr = qi >= 0 ? hash.slice(qi) : '';
        try {
            var p = new URLSearchParams(searchStr);
            return {
                seriesId: p.get('seriesId') || '',
                seriesName: p.get('seriesName') || 'Series'
            };
        } catch (e) {
            return { seriesId: '', seriesName: 'Series' };
        }
    }

    function rowClass(entry) {
        if (!entry.matched) return 'weo-row-unmatched';
        if (entry.confidence < 95) return 'weo-row-partial';
        return 'weo-row-matched';
    }

    function statusBadge(entry) {
        if (!entry.matched) return '<span class="weo-badge weo-badge-unmatched">Unmatched</span>';
        if (entry.confidence < 95) return '<span class="weo-badge weo-badge-partial">Partial</span>';
        return '<span class="weo-badge weo-badge-matched">Matched</span>';
    }

    function renderTable(entries) {
        var tbody = document.getElementById('previewTableBody');
        if (!tbody) return;

        tbody.innerHTML = entries.map(function (e) {
            var specialBadge = e.isSpecial ? '<span class="weo-badge weo-badge-special">Special</span>' : '';
            var confidence = e.matched ? Math.round(e.confidence) + '%' : '—';
            var jellyfinTitle = e.matched ? escapeHtml(e.jellyfinTitle || '') : '<em style="opacity:0.5">not found</em>';
            return '<tr class="' + rowClass(e) + '">' +
                '<td>' + e.position + '</td>' +
                '<td>' + escapeHtml(e.wikiTitle) + specialBadge + '</td>' +
                '<td>' + jellyfinTitle + '</td>' +
                '<td>' + statusBadge(e) + '</td>' +
                '<td>' + confidence + '</td>' +
                '<td>' + escapeHtml(e.matchMethod || '') + '</td>' +
                '</tr>';
        }).join('');
    }

    function showError(msg) {
        var err = document.getElementById('errorMsg');
        var errText = document.getElementById('errorText');
        if (err) err.style.display = 'block';
        if (errText) errText.textContent = msg;
        var load = document.getElementById('loadingMsg');
        if (load) load.style.display = 'none';
    }

    function loadPreview(seriesId) {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', ApiClient.getUrl(API_BASE + '/' + seriesId + '/preview'));
        xhr.setRequestHeader('X-Emby-Authorization', 'MediaBrowser Token="' + ApiClient.accessToken() + '", Client="Jellyfin Web", Device="Browser", DeviceId="plugin", Version="1.0.0"');
        xhr.onload = function () {
            if (xhr.status === 200) {
                var data = JSON.parse(xhr.responseText);
                document.getElementById('loadingMsg').style.display = 'none';
                document.getElementById('previewContent').style.display = 'block';
                document.getElementById('statMatched').textContent = data.matchedCount;
                document.getElementById('statUnmatched').textContent = data.unmatchedCount;
                document.getElementById('statTotal').textContent = (data.entries || []).length;
                document.getElementById('statRefresh').textContent = formatDate(data.lastRefreshUtc);
                renderTable(data.entries || []);
            } else {
                showError('Could not load preview. Status ' + xhr.status + '. Try refreshing the series from Wikipedia first.');
            }
        };
        xhr.onerror = function () {
            showError('Could not load preview. Network error. Try refreshing the series from Wikipedia first.');
        };
        xhr.send();
    }

    function refreshNow(seriesId) {
        Dashboard.showLoadingMsg();
        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl(API_BASE + '/' + seriesId + '/refresh') })
            .then(function () {
                Dashboard.hideLoadingMsg();
                loadPreview(seriesId);
            })
            .catch(function (err) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Refresh failed: ' + (err.statusText || err));
            });
    }

    var page = document.getElementById('WikipediaEpisodeOrderPreviewPage');
    if (page) {
        page.addEventListener('viewshow', function () {
            var params = getParams();
            var seriesId = params.seriesId;
            var seriesName = params.seriesName;

            var titleEl = document.getElementById('pageTitle');
            if (titleEl) titleEl.textContent = 'Episode Order Preview — ' + seriesName;

            var btnRefresh = document.getElementById('btnRefreshNow');
            if (btnRefresh) {
                btnRefresh.onclick = function () {
                    if (seriesId) refreshNow(seriesId);
                };
            }

            if (!seriesId) {
                showError('No series ID specified. Return to the configuration page and click Preview.');
                return;
            }

            loadPreview(seriesId);
        });
    }

})();
