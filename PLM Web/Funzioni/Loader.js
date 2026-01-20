/* ========================================
   LOADER - PLM 2
   Funzioni per gestione stati di caricamento
   ======================================== */

function showLoader(containerId, message = 'Caricamento...') {
    const container = document.getElementById(containerId);
    if (container) {
        container.innerHTML = `<div class="loading-state">${message}</div>`;
    }
}

function showEmptyState(containerId, message = 'Nessun dato disponibile', icon = '📋') {
    const container = document.getElementById(containerId);
    if (container) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">${icon}</div>
                <div class="empty-text">${message}</div>
            </div>
        `;
    }
}

function showErrorState(containerId, message) {
    const container = document.getElementById(containerId);
    if (container) {
        container.innerHTML = `
            <div class="empty-state">
                <div class="empty-icon">⚠️</div>
                <div class="empty-text">Errore: ${message}</div>
            </div>
        `;
    }
}

// Esponi globalmente
window.showLoader = showLoader;
window.showEmptyState = showEmptyState;
window.showErrorState = showErrorState;
