/* Wikipedia Episode Order - Preview Page */
(function () {
    'use strict';

    var apiBase = '/WikipediaOrder';

    function qs(id) { return document.getElementById(id); }

    function escapeHtml(str) {
        return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function getQueryParam(name) {
        var params = new URLSearchParams(window.location.search);
        return params.get(name);
    }

    function formatDate(isoString) {
        if (!isoString) return '—';
        try {
            var d = new Date(isoString);
            return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
        } catch (e) { return isoString; }
    }

    function rowClass(entry) {
        if (!entry.matched)                  return 'weo-row-unmatched';
        if (entry.confidence < 95)           return 'weo-row-partial';
        return 'weo-row-matched';
    }

    function statusBadge(entry) {
        if (!entry.matched)         return '<span class="weo-badge weo-badge-unmatched">Unmatched</span>';
        if (entry.confidence < 95)  return '<span class="weo-badge weo-badge-partial">Partial</span>';
        return '<span class="weo-badge weo-badge-matched">Matched</span>';
    }

    function renderTable(entries) {
        var tbody = qs('previewTableBody');
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
        var err = qs('errorMsg');
        var errText = qs('errorText');
        if (err)     err.style.display = 'block';
        if (errText) errText.textContent = msg;
        var load = qs('loadingMsg');
        if (load) load.style.display = 'none';
    }

    function loadPreview(seriesId) {
        var url = ApiClient.getUrl(apiBase + '/' + seriesId + '/preview');
        ApiClient.ajax({ type: 'GET', url: url, dataType: 'json' })
            .then(function (data) {
                qs('loadingMsg').style.display = 'none';
                qs('previewContent').style.display = 'block';

                qs('statMatched').textContent  = data.matchedCount;
                qs('statUnmatched').textContent = data.unmatchedCount;
                qs('statTotal').textContent    = (data.entries || []).length;
                qs('statRefresh').textContent  = formatDate(data.lastRefreshUtc);

                renderTable(data.entries || []);
            })
            .catch(function (err) {
                showError('Could not load preview. Status ' + (err.status || 'unknown') +
                    '. Try refreshing the series from Wikipedia first.');
            });
    }

    function refreshNow(seriesId) {
        Dashboard.showLoadingMsg();
        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl(apiBase + '/' + seriesId + '/refresh') })
            .then(function () {
                Dashboard.hideLoadingMsg();
                loadPreview(seriesId);
            })
            .catch(function (err) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Refresh failed: ' + (err.statusText || err));
            });
    }

    function init() {
        var seriesId   = getQueryParam('seriesId');
        var seriesName = getQueryParam('seriesName') || 'Series';

        var titleEl = qs('pageTitle');
        if (titleEl) titleEl.textContent = 'Episode Order Preview — ' + decodeURIComponent(seriesName);

        var btnRefresh = qs('btnRefreshNow');
        if (btnRefresh) {
            btnRefresh.addEventListener('click', function () {
                if (seriesId) refreshNow(seriesId);
            });
        }

        if (!seriesId) {
            showError('No series ID specified. Return to the configuration page and click Preview.');
            return;
        }

        loadPreview(seriesId);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

})();
