/* Wikipedia Episode Order - Preview Page */

function escapeHtml(str) {
    return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function formatDate(isoString) {
    if (!isoString) return '—';
    try {
        return new Date(isoString).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
    } catch (e) { return isoString; }
}

export default class WikipediaEpisodeOrderPreviewPage {
    constructor(view, params) {
        this._view = view;
        this._apiBase = '/WikipediaOrder';
        this._seriesId = params.seriesId || '';
        this._seriesName = params.seriesName || 'Series';

        view.addEventListener('viewshow', () => {
            this.init();
        });
    }

    qs(id) { return this._view.querySelector('#' + id); }

    rowClass(entry) {
        if (!entry.Matched) return 'weo-row-unmatched';
        if (entry.Confidence < 95) return 'weo-row-partial';
        return 'weo-row-matched';
    }

    statusBadge(entry) {
        if (!entry.Matched) return '<span class="weo-badge weo-badge-unmatched">Unmatched</span>';
        if (entry.Confidence < 95) return '<span class="weo-badge weo-badge-partial">Partial</span>';
        return '<span class="weo-badge weo-badge-matched">Matched</span>';
    }

    renderTable(entries) {
        var tbody = this.qs('previewTableBody');
        if (!tbody) return;

        tbody.innerHTML = entries.map((e) => {
            var specialBadge = e.IsSpecial ? '<span class="weo-badge weo-badge-special">Special</span>' : '';
            var confidence = e.Matched ? Math.round(e.Confidence) + '%' : '—';
            var jellyfinTitle = e.Matched ? escapeHtml(e.JellyfinTitle || '') : '<em style="opacity:0.5">not found</em>';
            return '<tr class="' + this.rowClass(e) + '">' +
                '<td>' + e.Position + '</td>' +
                '<td>' + escapeHtml(e.WikiTitle) + specialBadge + '</td>' +
                '<td>' + jellyfinTitle + '</td>' +
                '<td>' + this.statusBadge(e) + '</td>' +
                '<td>' + confidence + '</td>' +
                '<td>' + escapeHtml(e.MatchMethod || '') + '</td>' +
                '</tr>';
        }).join('');
    }

    showError(msg) {
        var err = this.qs('errorMsg');
        var errText = this.qs('errorText');
        if (err) err.style.display = 'block';
        if (errText) errText.textContent = msg;
        var load = this.qs('loadingMsg');
        if (load) load.style.display = 'none';
    }

    loadPreview() {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', ApiClient.getUrl(this._apiBase + '/' + this._seriesId + '/preview'));
        xhr.setRequestHeader('X-Emby-Authorization', 'MediaBrowser Token="' + ApiClient.accessToken() + '", Client="Jellyfin Web", Device="Browser", DeviceId="plugin", Version="1.0.0"');
        xhr.onload = () => {
            if (xhr.status === 200) {
                var data = JSON.parse(xhr.responseText);
                this.qs('loadingMsg').style.display = 'none';
                this.qs('previewContent').style.display = 'block';
                this.qs('statMatched').textContent = data.MatchedCount;
                this.qs('statUnmatched').textContent = data.UnmatchedCount;
                this.qs('statTotal').textContent = (data.Entries || []).length;
                this.qs('statRefresh').textContent = formatDate(data.LastRefreshUtc);
                this.renderTable(data.Entries || []);
            } else {
                this.showError('Could not load preview. Status ' + xhr.status + '. Try refreshing the series from Wikipedia first.');
            }
        };
        xhr.onerror = () => {
            this.showError('Could not load preview. Network error. Try refreshing the series from Wikipedia first.');
        };
        xhr.send();
    }

    refreshNow() {
        Dashboard.showLoadingMsg();
        var xhr = new XMLHttpRequest();
        xhr.open('POST', ApiClient.getUrl(this._apiBase + '/' + this._seriesId + '/refresh'));
        xhr.setRequestHeader('X-Emby-Authorization', 'MediaBrowser Token="' + ApiClient.accessToken() + '", Client="Jellyfin Web", Device="Browser", DeviceId="plugin", Version="1.0.0"');
        xhr.onload = () => {
            Dashboard.hideLoadingMsg();
            if (xhr.status === 200 || xhr.status === 202 || xhr.status === 204) {
                this.loadPreview();
            } else {
                Dashboard.alert('Refresh failed: HTTP ' + xhr.status);
            }
        };
        xhr.onerror = () => {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Refresh failed: Network error.');
        };
        xhr.send();
    }

    init() {
        var titleEl = this.qs('pageTitle');
        if (titleEl) titleEl.textContent = 'Episode Order Preview — ' + this._seriesName;

        var btnRefresh = this.qs('btnRefreshNow');
        if (btnRefresh) {
            btnRefresh.addEventListener('click', () => {
                if (this._seriesId) this.refreshNow();
            });
        }

        if (!this._seriesId) {
            this.showError('No series ID specified. Return to the configuration page and click Preview.');
            return;
        }

        this.loadPreview();
    }
}
