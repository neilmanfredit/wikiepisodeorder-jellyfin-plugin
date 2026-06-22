/* Wikipedia Episode Order - Configuration Page */
(function () {
    'use strict';

    var apiBase = '/WikipediaOrder';
    var mappings = [];
    var editingIndex = -1;

    function getPageElement(view) {
        return view || document;
    }

    function qs(selector, context) {
        return (context || document).querySelector(selector);
    }

    function renderMappings() {
        var list = qs('#mappingList');
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
        var form = qs('#mappingForm');
        if (!form) return;
        form.style.display = 'block';
        editingIndex = (typeof index === 'number') ? index : -1;

        if (editingIndex >= 0 && mappings[editingIndex]) {
            var m = mappings[editingIndex];
            qs('#mappingFormTitle').textContent = 'Edit Mapping';
            qs('#seriesId').value = m.seriesId || '';
            qs('#seriesName').value = m.seriesName || '';
            qs('#wikiUrl').value = m.wikipediaUrl || '';
            qs('#autoRefresh').checked = !!m.autoRefresh;
            qs('#refreshDays').value = m.refreshDays || 7;
        } else {
            qs('#mappingFormTitle').textContent = 'Add Mapping';
            qs('#seriesId').value = '';
            qs('#seriesName').value = '';
            qs('#wikiUrl').value = '';
            qs('#autoRefresh').checked = false;
            qs('#refreshDays').value = 7;
        }

        form.scrollIntoView({ behavior: 'smooth' });
    }

    function hideForm() {
        var form = qs('#mappingForm');
        if (form) form.style.display = 'none';
        editingIndex = -1;
    }

    function saveMapping() {
        var seriesId = (qs('#seriesId').value || '').trim();
        var seriesName = (qs('#seriesName').value || '').trim();
        var wikiUrl = (qs('#wikiUrl').value || '').trim();
        var autoRefresh = qs('#autoRefresh').checked;
        var refreshDays = parseInt(qs('#refreshDays').value, 10) || 7;

        if (!seriesId || !seriesName || !wikiUrl) {
            alert('Series ID, Series Name, and Wikipedia URL are all required.');
            return;
        }

        var entry = { seriesId: seriesId, seriesName: seriesName, wikipediaUrl: wikiUrl, autoRefresh: autoRefresh, refreshDays: refreshDays, lastUpdatedUtc: null };

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
        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl(apiBase + '/' + m.seriesId + '/refresh') })
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
        window.location.href = 'orderPreview.html?seriesId=' + encodeURIComponent(m.seriesId) + '&seriesName=' + encodeURIComponent(m.seriesName);
    }

    function saveConfiguration() {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration('a8cba4a4-65b8-4a7e-9f0e-b3e56b6e3b7b').then(function (config) {
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
            ApiClient.updatePluginConfiguration('a8cba4a4-65b8-4a7e-9f0e-b3e56b6e3b7b', config).then(function () {
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Configuration saved.');
            });
        }).catch(function (err) {
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Save failed: ' + (err.statusText || err));
        });
    }

    function loadConfiguration() {
        ApiClient.getPluginConfiguration('a8cba4a4-65b8-4a7e-9f0e-b3e56b6e3b7b').then(function (config) {
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

    function escapeHtml(str) {
        return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function attachDelegatedClickHandler() {
        var container = document.getElementById('WikipediaEpisodeOrderConfigPage');
        if (!container) return;
        container.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-action]');
            if (!btn) return;
            var action = btn.getAttribute('data-action');
            var index = parseInt(btn.getAttribute('data-index'), 10);
            if (action === 'edit')           showForm(index);
            if (action === 'delete')         deleteMapping(index);
            if (action === 'refresh')        refreshMapping(index);
            if (action === 'preview')        previewMapping(index);
            if (action === 'add-mapping')    showForm();
            if (action === 'save-mapping')   saveMapping();
            if (action === 'cancel-mapping') hideForm();
            if (action === 'save-all')       saveConfiguration();
        }, true);
    }

    // Load on page enter
    document.addEventListener('pageshow', function (e) {
        if (e.target && e.target.id === 'WikipediaEpisodeOrderConfigPage') {
            attachDelegatedClickHandler();
            loadConfiguration();
        }
    });

    // Immediate load if document already visible
    if (document.readyState === 'complete' || document.readyState === 'interactive') {
        if (document.getElementById('WikipediaEpisodeOrderConfigPage')) {
            attachDelegatedClickHandler();
        }
        loadConfiguration();
    }

})();
