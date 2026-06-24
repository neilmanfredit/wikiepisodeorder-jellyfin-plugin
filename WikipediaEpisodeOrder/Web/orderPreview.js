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
        if (!entry.matched) return 'weo-row-unmatched';
        if (entry.confidence < 95) return 'weo-row-partial';
        return 'weo-row-matched';
    }

    statusBadge(entry) {
        if (!entry.matched) return '<span class="weo-badge weo-badge-unmatched">Unmatched</span>';
        if (entry.confidence < 95) return '<span class="weo-badge weo-badge-partial">Partial</span>';
        return '<span class="weo-badge weo-badge-matched">Matched</span>';
    }

    renderTable(entries) {
        var tbody = this.qs('previewTableBody');
        if (!tbody) return;

        tbody.innerHTML = entries.map((e) => {
            var specialBadge = e.isSpecial ? '<span class="weo-badge weo-badge-special">Special</span>' : '';
            var confidence = e.matched ? Math.round(e.confidence) + '%' : '—';
            var jellyfinTitle = e.matched ? escapeHtml(e.jellyfinTitle || '') : '<em style="opacity:0.5">not found</em>';
            return '<tr class="' + this.rowClass(e) + '">' +
                '<td>' + e.position + '</td>' +
                '<td>' + escapeHtml(e.wikiTitle) + specialBadge + '</td>' +
                '<td>' + jellyfinTitle + '</td>' +
                '<td>' + this.statusBadge(e) + '</td>' +
                '<td>' + confidence + '</td>' +
                '<td>' + escapeHtml(e.matchMethod || '') + '</td>' +
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
        var url = ApiClient.getUrl(this._apiBase + '/' + this._seriesId + '/preview');
        ApiClient.ajax({ type: 'GET', url: url })
            .then((r) => r.json())
            .then((data) => {
                this.qs('loadingMsg').style.display = 'none';
                this.qs('previewContent').style.display = 'block';
                this.qs('statMatched').textContent = data.matchedCount;
                this.qs('statUnmatched').textContent = data.unmatchedCount;
                this.qs('statTotal').textContent = (data.entries || []).length;
                this.qs('statRefresh').textContent = formatDate(data.lastRefreshUtc);
                this.renderTable(data.entries || []);
            })
            .catch((err) => {
                this.showError('Could not load preview. Status ' + (err.status || 'unknown') +
                    '. Try refreshing the series from Wikipedia first.');
            });
    }

    refreshNow() {
        Dashboard.showLoadingMsg();
        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl(this._apiBase + '/' + this._seriesId + '/refresh') })
            .then(() => {
                Dashboard.hideLoadingMsg();
                this.loadPreview();
            })
            .catch((err) => {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Refresh failed: ' + (err.statusText || err));
            });
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
