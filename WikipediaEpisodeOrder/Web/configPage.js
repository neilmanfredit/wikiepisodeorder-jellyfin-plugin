/* Wikipedia Episode Order - Configuration Page */
(function () {
    'use strict';

    var mappings = [];
    var editingIndex = -1;
    var searchTimeout = null;
    var searchInitialized = false;
    var API_BASE = '/WikipediaOrder';
    var PLUGIN_GUID = 'a8cba4a4-65b8-4a7e-9f0e-b3e56b6e3b7b';

    function escapeHtml(str) {
        return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function searchSeries(q, callback) {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', ApiClient.getUrl(API_BASE + '/series/search?q=' + encodeURIComponent(q)));
        xhr.setRequestHeader('X-Emby-Authorization', 'MediaBrowser Token="' + ApiClient.accessToken() + '", Client="Jellyfin Web", Device="Browser", DeviceId="plugin", Version="1.0.0"');
        xhr.onload = function () {
            if (xhr.status === 200) callback(JSON.parse(xhr.responseText));
            else callback([]);
        };
        xhr.onerror = function () { callback([]); };
        xhr.send();
    }

    function renderMappings() {
        var list = document.getElementById('mappingList');
        if (!list) return;

        if (mappings.length === 0) {
            list.innerHTML = '<p class="fieldDescription">No series mappings configured.</p>';
            return;
        }

        list.innerHTML = mappings.map(function (m, i) {
            return '<div class="listItem listItem-border" style="padding:1em;margin-bottom:0.5em;">' +
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
                '</div>';
        }).join('');
    }

    function showForm(index) {
        var form = document.getElementById('mappingForm');
        if (!form) return;
        form.style.display = 'block';
        editingIndex = (typeof index === 'number') ? index : -1;

        if (editingIndex >= 0 && mappings[editingIndex]) {
            var m = mappings[editingIndex];
            document.getElementById('mappingFormTitle').textContent = 'Edit Mapping';
            document.getElementById('seriesId').value = m.seriesId || '';
            var ss = document.getElementById('seriesSearch');
            if (ss) ss.value = m.seriesName || '';
            document.getElementById('seriesName').value = m.seriesName || '';
            document.getElementById('wikiUrl').value = m.wikipediaUrl || '';
            document.getElementById('autoRefresh').checked = !!m.autoRefresh;
            document.getElementById('refreshDays').value = m.refreshDays || 7;
        } else {
            document.getElementById('mappingFormTitle').textContent = 'Add Mapping';
            document.getElementById('seriesId').value = '';
            var ss2 = document.getElementById('seriesSearch');
            if (ss2) ss2.value = '';
            document.getElementById('seriesName').value = '';
            document.getElementById('wikiUrl').value = '';
            document.getElementById('autoRefresh').checked = false;
            document.getElementById('refreshDays').value = 7;
        }

        form.scrollIntoView({ behavior: 'smooth' });
    }

    function hideForm() {
        var form = document.getElementById('mappingForm');
        if (form) form.style.display = 'none';
        var resultsDiv = document.getElementById('seriesSearchResults');
        if (resultsDiv) resultsDiv.style.display = 'none';
        editingIndex = -1;
    }

    function saveMapping() {
        var seriesId = (document.getElementById('seriesId').value || '').trim();
        var seriesName = (document.getElementById('seriesName').value || '').trim();
        var wikiUrl = (document.getElementById('wikiUrl').value || '').trim();
        var autoRefresh = document.getElementById('autoRefresh').checked;
        var refreshDays = parseInt(document.getElementById('refreshDays').value, 10) || 7;

        if (!seriesId || !seriesName || !wikiUrl) {
            alert('Please select a series from the search results, enter a Series Name, and provide a Wikipedia URL.');
            return;
        }

        var entry = {
            seriesId: seriesId,
            seriesName: seriesName,
            wikipediaUrl: wikiUrl,
            autoRefresh: autoRefresh,
            refreshDays: refreshDays,
            lastUpdatedUtc: null
        };

        if (editingIndex >= 0) {
            entry.lastUpdatedUtc = (mappings[editingIndex] || {}).lastUpdatedUtc || null;
            mappings[editingIndex] = entry;
        } else {
            mappings.push(entry);
        }

        hideForm();
        renderMappings();
    }

    function deleteMapping(index) {
        if (!confirm('Remove mapping for "' + escapeHtml(mappings[index].seriesName) + '"?')) return;
        mappings.splice(index, 1);
        renderMappings();
    }

    function refreshMapping(index) {
        var m = mappings[index];
        if (!m || !m.seriesId) { alert('Save configuration before refreshing.'); return; }
        Dashboard.showLoadingMsg();
        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl(API_BASE + '/' + m.seriesId + '/refresh') })
            .then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Refresh complete for "' + m.seriesName + '".');
            })
            .catch(function (err) {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Refresh failed: ' + (err.statusText || err));
            });
    }

    function previewMapping(index) {
        var m = mappings[index];
        if (!m || !m.seriesId) { alert('Save configuration and refresh before previewing.'); return; }
        Dashboard.navigate('configurationpage?name=WikipediaEpisodeOrderPreview&seriesId=' +
            encodeURIComponent(m.seriesId) + '&seriesName=' + encodeURIComponent(m.seriesName));
    }

    function saveConfiguration() {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(PLUGIN_GUID).then(function (config) {
            config.Mappings = mappings.map(function (m) {
                return {
                    SeriesId: m.seriesId,
                    SeriesName: m.seriesName,
                    WikipediaUrl: m.wikipediaUrl,
                    AutoRefresh: m.autoRefresh,
                    RefreshDays: m.refreshDays,
                    LastUpdatedUtc: m.lastUpdatedUtc
                };
            });
            ApiClient.updatePluginConfiguration(PLUGIN_GUID, config).then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Configuration saved.');
            });
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Save failed: ' + (err.statusText || err));
        });
    }

    function loadConfiguration() {
        ApiClient.getPluginConfiguration(PLUGIN_GUID).then(function (config) {
            mappings = (config.Mappings || []).map(function (m) {
                return {
                    seriesId: m.SeriesId,
                    seriesName: m.SeriesName,
                    wikipediaUrl: m.WikipediaUrl,
                    autoRefresh: m.AutoRefresh,
                    refreshDays: m.RefreshDays,
                    lastUpdatedUtc: m.LastUpdatedUtc
                };
            });
            renderMappings();
        }).catch(function () {
            mappings = [];
            renderMappings();
        });
    }

    function selectSeries(id, name) {
        document.getElementById('seriesId').value = id;
        var ss = document.getElementById('seriesSearch');
        if (ss) ss.value = name;
        var nameInput = document.getElementById('seriesName');
        if (nameInput && !nameInput.value) nameInput.value = name;
        var resultsDiv = document.getElementById('seriesSearchResults');
        if (resultsDiv) resultsDiv.style.display = 'none';
    }

    function initSearchBox() {
        if (searchInitialized) return;
        var searchInput = document.getElementById('seriesSearch');
        var resultsDiv = document.getElementById('seriesSearchResults');
        if (!searchInput || !resultsDiv) return;
        searchInitialized = true;

        searchInput.addEventListener('input', function () {
            clearTimeout(searchTimeout);
            var q = searchInput.value.trim();
            if (q.length < 2) { resultsDiv.style.display = 'none'; return; }
            searchTimeout = setTimeout(function () {
                searchSeries(q, function (data) {
                    if (!data || data.length === 0) {
                        resultsDiv.innerHTML = '<div style="padding:0.5em;color:#888;">No results</div>';
                    } else {
                        resultsDiv.innerHTML = data.map(function (item) {
                            return '<div style="padding:0.6em 1em;cursor:pointer;" ' +
                                'data-id="' + escapeHtml(item.Id) + '" ' +
                                'data-name="' + escapeHtml(item.Name) + '" ' +
                                'onmousedown="WikipediaEpisodeOrderPage.selectSeries(\'' + escapeHtml(item.Id) + '\',\'' + escapeHtml((item.Name || '').replace(/'/g, "\\'")) + '\')">' +
                                escapeHtml(item.Name) + '</div>';
                        }).join('');
                    }
                    resultsDiv.style.display = 'block';
                });
            }, 300);
        });

        searchInput.addEventListener('blur', function () {
            setTimeout(function () { resultsDiv.style.display = 'none'; }, 200);
        });
    }

    window.WikipediaEpisodeOrderPage = {
        showForm: showForm,
        hideForm: hideForm,
        saveMapping: saveMapping,
        deleteMapping: deleteMapping,
        refreshMapping: refreshMapping,
        previewMapping: previewMapping,
        saveConfiguration: saveConfiguration,
        loadConfiguration: loadConfiguration,
        selectSeries: selectSeries
    };

    var page = document.getElementById('WikipediaEpisodeOrderConfigPage');
    if (page) {
        page.addEventListener('viewshow', function () {
            loadConfiguration();
            initSearchBox();
        });

        page.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action]');
            if (!btn) return;
            var action = btn.getAttribute('data-action');
            var index = parseInt(btn.getAttribute('data-index'), 10);
            if (action === 'preview') previewMapping(index);
            else if (action === 'refresh') refreshMapping(index);
            else if (action === 'edit') showForm(index);
            else if (action === 'delete') deleteMapping(index);
        });
    }

})();
