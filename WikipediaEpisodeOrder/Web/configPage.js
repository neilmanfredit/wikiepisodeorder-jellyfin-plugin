/* Wikipedia Episode Order - Configuration Page */

function escapeHtml(str) {
    return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

export default class WikipediaEpisodeOrderConfigPage {
    constructor(view, params) {
        this._view = view;
        this._mappings = [];
        this._editingIndex = -1;
        this._apiBase = '/WikipediaOrder';
        this._searchTimeout = null;
        this._searchInitialized = false;

        window.WikipediaEpisodeOrderPage = {
            showForm: (index) => this.showForm(index),
            hideForm: () => this.hideForm(),
            saveMapping: () => this.saveMapping(),
            deleteMapping: (index) => this.deleteMapping(index),
            refreshMapping: (index) => this.refreshMapping(index),
            previewMapping: (index) => this.previewMapping(index),
            saveConfiguration: () => this.saveConfiguration(),
            loadConfiguration: () => this.loadConfiguration(),
            selectSeries: (id, name) => this.selectSeries(id, name)
        };

        view.addEventListener('viewshow', () => {
            this.loadConfiguration();
            this.initSearchBox();
        });

        view.addEventListener('click', (e) => {
            var btn = e.target.closest('[data-action]');
            if (!btn) return;
            var action = btn.getAttribute('data-action');
            var index = parseInt(btn.getAttribute('data-index'), 10);
            if (action === 'preview') this.previewMapping(index);
            else if (action === 'refresh') this.refreshMapping(index);
            else if (action === 'edit') this.showForm(index);
            else if (action === 'delete') this.deleteMapping(index);
        });
    }

    qs(selector) {
        return this._view.querySelector(selector);
    }

    renderMappings() {
        var list = this.qs('#mappingList');
        if (!list) return;

        if (this._mappings.length === 0) {
            list.innerHTML = '<p class="fieldDescription">No series mappings configured.</p>';
            return;
        }

        list.innerHTML = this._mappings.map((m, i) =>
            '<div class="listItem listItem-border" style="padding:1em;margin-bottom:0.5em;">' +
            '<div class="listItemBody">' +
            '<h3 class="listItemBodyText">' + escapeHtml(m.seriesName || 'Unnamed') + '</h3>' +
            '<div class="listItemBodyText secondary">' + escapeHtml(m.wikipediaUrl || '') + '</div>' +
            '<div class="listItemBodyText secondary">Auto refresh: ' + (m.autoRefresh ? 'Every ' + m.refreshDays + ' days' : 'Off') + '</div>' +
            '</div>' +
            '<div class="listItemButtons">' +
            '<button is="emby-button" data-action="preview" data-index="' + i + '" class="listItemButton paper-icon-button-light" title="Preview Order"><span class="material-icons">visibility</span></button>' +
            '<button is="emby-button" data-action="refresh" data-index="' + i + '" class="listItemButton paper-icon-button-light" title="Refresh Now"><span class="material-icons">refresh</span></button>' +
            '<button is="emby-button" data-action="edit" data-index="' + i + '" class="listItemButton paper-icon-button-light" title="Edit"><span class="material-icons">edit</span></button>' +
            '<button is="emby-button" data-action="delete" data-index="' + i + '" class="listItemButton paper-icon-button-light" title="Delete"><span class="material-icons">delete</span></button>' +
            '</div>' +
            '</div>'
        ).join('');
    }

    showForm(index) {
        var form = this.qs('#mappingForm');
        if (!form) return;
        form.style.display = 'block';
        this._editingIndex = (typeof index === 'number') ? index : -1;

        if (this._editingIndex >= 0 && this._mappings[this._editingIndex]) {
            var m = this._mappings[this._editingIndex];
            this.qs('#mappingFormTitle').textContent = 'Edit Mapping';
            var ss = this.qs('#seriesSearch');
            if (ss) ss.value = m.seriesName || '';
            this.qs('#seriesId').value = m.seriesId || '';
            this.qs('#seriesName').value = m.seriesName || '';
            this.qs('#wikiUrl').value = m.wikipediaUrl || '';
            this.qs('#autoRefresh').checked = !!m.autoRefresh;
            this.qs('#refreshDays').value = m.refreshDays || 7;
        } else {
            this.qs('#mappingFormTitle').textContent = 'Add Mapping';
            var ss2 = this.qs('#seriesSearch');
            if (ss2) ss2.value = '';
            this.qs('#seriesId').value = '';
            this.qs('#seriesName').value = '';
            this.qs('#wikiUrl').value = '';
            this.qs('#autoRefresh').checked = false;
            this.qs('#refreshDays').value = 7;
        }

        form.scrollIntoView({ behavior: 'smooth' });
    }

    hideForm() {
        var form = this.qs('#mappingForm');
        if (form) form.style.display = 'none';
        var resultsDiv = this.qs('#seriesSearchResults');
        if (resultsDiv) resultsDiv.style.display = 'none';
        this._editingIndex = -1;
    }

    saveMapping() {
        var seriesId = (this.qs('#seriesId').value || '').trim();
        var seriesName = (this.qs('#seriesName').value || '').trim();
        var wikiUrl = (this.qs('#wikiUrl').value || '').trim();
        var autoRefresh = this.qs('#autoRefresh').checked;
        var refreshDays = parseInt(this.qs('#refreshDays').value, 10) || 7;

        if (!seriesId || !seriesName || !wikiUrl) {
            alert('Please select a series from the search results, enter a Series Name, and provide a Wikipedia URL.');
            return;
        }

        var entry = { seriesId, seriesName, wikipediaUrl: wikiUrl, autoRefresh, refreshDays, lastUpdatedUtc: null };

        if (this._editingIndex >= 0) {
            entry.lastUpdatedUtc = (this._mappings[this._editingIndex] || {}).lastUpdatedUtc || null;
            this._mappings[this._editingIndex] = entry;
        } else {
            this._mappings.push(entry);
        }

        this.hideForm();
        this.renderMappings();
    }

    deleteMapping(index) {
        if (!confirm('Remove mapping for "' + escapeHtml(this._mappings[index].seriesName) + '"?')) return;
        this._mappings.splice(index, 1);
        this.renderMappings();
    }

    refreshMapping(index) {
        var m = this._mappings[index];
        if (!m || !m.seriesId) { alert('Save configuration before refreshing.'); return; }
        Dashboard.showLoadingMsg();
        var xhr = new XMLHttpRequest();
        xhr.open('POST', ApiClient.getUrl(this._apiBase + '/' + m.seriesId + '/refresh'));
        xhr.setRequestHeader('X-Emby-Authorization', 'MediaBrowser Token="' + ApiClient.accessToken() + '", Client="Jellyfin Web", Device="Browser", DeviceId="plugin", Version="1.0.0"');
        xhr.onload = () => {
            Dashboard.hideLoadingMsg();
            if (xhr.status === 200 || xhr.status === 202 || xhr.status === 204) {
                Dashboard.alert('Refresh complete for "' + m.seriesName + '".');
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

    previewMapping(index) {
        var m = this._mappings[index];
        if (!m || !m.seriesId) { alert('Save configuration and refresh before previewing.'); return; }
        Dashboard.navigate('configurationpage?name=WikipediaEpisodeOrderPreview&seriesId=' +
            encodeURIComponent(m.seriesId) + '&seriesName=' + encodeURIComponent(m.seriesName));
    }

    saveConfiguration() {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration('a8cba4a4-65b8-4a7e-9f0e-b3e56b6e3b7b').then((config) => {
            config.Mappings = this._mappings.map((m) => ({
                SeriesId: m.seriesId,
                SeriesName: m.seriesName,
                WikipediaUrl: m.wikipediaUrl,
                AutoRefresh: m.autoRefresh,
                RefreshDays: m.refreshDays,
                LastUpdatedUtc: m.lastUpdatedUtc
            }));
            ApiClient.updatePluginConfiguration('a8cba4a4-65b8-4a7e-9f0e-b3e56b6e3b7b', config).then(() => {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Configuration saved.');
            });
        }).catch((err) => {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Save failed: ' + (err.statusText || err));
        });
    }

    loadConfiguration() {
        ApiClient.getPluginConfiguration('a8cba4a4-65b8-4a7e-9f0e-b3e56b6e3b7b').then((config) => {
            this._mappings = (config.Mappings || []).map((m) => ({
                seriesId: m.SeriesId,
                seriesName: m.SeriesName,
                wikipediaUrl: m.WikipediaUrl,
                autoRefresh: m.AutoRefresh,
                refreshDays: m.RefreshDays,
                lastUpdatedUtc: m.LastUpdatedUtc
            }));
            this.renderMappings();
        }).catch(() => {
            this._mappings = [];
            this.renderMappings();
        });
    }

    selectSeries(id, name) {
        this.qs('#seriesId').value = id;
        var ss = this.qs('#seriesSearch');
        if (ss) ss.value = name;
        var nameInput = this.qs('#seriesName');
        if (nameInput && !nameInput.value) nameInput.value = name;
        var resultsDiv = this.qs('#seriesSearchResults');
        if (resultsDiv) resultsDiv.style.display = 'none';
    }

    initSearchBox() {
        if (this._searchInitialized) return;
        var searchInput = this.qs('#seriesSearch');
        var resultsDiv = this.qs('#seriesSearchResults');
        if (!searchInput || !resultsDiv) return;
        this._searchInitialized = true;

        searchInput.addEventListener('input', () => {
            clearTimeout(this._searchTimeout);
            var q = searchInput.value.trim();
            if (q.length < 2) { resultsDiv.style.display = 'none'; return; }
            this._searchTimeout = setTimeout(() => {
                var xhr = new XMLHttpRequest();
                xhr.open('GET', ApiClient.getUrl(this._apiBase + '/series/search?q=' + encodeURIComponent(q)));
                xhr.setRequestHeader('X-Emby-Authorization', 'MediaBrowser Token="' + ApiClient.accessToken() + '", Client="Jellyfin Web", Device="Browser", DeviceId="plugin", Version="1.0.0"');
                xhr.onload = () => {
                    var data = [];
                    if (xhr.status === 200) {
                        try { data = JSON.parse(xhr.responseText); } catch (e) {}
                    }
                    if (!data || data.length === 0) {
                        resultsDiv.innerHTML = '<div style="padding:0.5em;color:#888;">No results</div>';
                    } else {
                        resultsDiv.innerHTML = data.map((item) =>
                            '<div style="padding:0.6em 1em;cursor:pointer;" ' +
                            'data-id="' + escapeHtml(item.Id) + '" ' +
                            'data-name="' + escapeHtml(item.Name) + '" ' +
                            'onmousedown="WikipediaEpisodeOrderPage.selectSeries(\'' + escapeHtml(item.Id) + '\',\'' + escapeHtml((item.Name || '').replace(/'/g, "\\'")) + '\')">' +
                            escapeHtml(item.Name) + '</div>'
                        ).join('');
                    }
                    resultsDiv.style.display = 'block';
                };
                xhr.onerror = () => {
                    resultsDiv.innerHTML = '<div style="padding:0.5em;color:#888;">Search failed</div>';
                    resultsDiv.style.display = 'block';
                };
                xhr.send();
            }, 300);
        });

        searchInput.addEventListener('blur', () => {
            setTimeout(() => { resultsDiv.style.display = 'none'; }, 200);
        });
    }
}
