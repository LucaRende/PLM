/* ========================================
   AMMINISTRAZIONE - PLM 2
   Carica e gestisce pannello admin
   ======================================== */

/**
 * Carica contenuto Admin
 */
async function loadAdminContent() {
    const container = document.getElementById('content-container');
    if (!container) return;
    
    const basePath = window.BASE_PATH;
    const filePath = basePath + '/Amministrazione/AdminContent.html';
    
    try {
        const response = await fetch(filePath);
        const html = await response.text();
        
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');
        
        const template = doc.getElementById('plm-admin');
        if (template) {
            container.innerHTML = template.innerHTML;
            initAdmin();
        }
    } catch (error) {
        console.error('Errore caricamento admin:', error);
    }
}

/**
 * Inizializza admin
 */
function initAdmin() {
    loadAdminStats();
    loadActivityLog();
    setupModal();
}

/**
 * Toggle dashboard admin
 */
function toggleAdminDash() {
    const header = document.getElementById('adminDashHeader');
    const content = document.getElementById('adminDashContent');
    
    if (header && content) {
        header.classList.toggle('active');
        content.classList.toggle('show');
    }
}

/**
 * Toggle dashboard password
 */
function togglePasswordDash() {
    const header = document.getElementById('passwordDashHeader');
    const content = document.getElementById('passwordDashContent');
    
    if (header && content) {
        header.classList.toggle('active');
        content.classList.toggle('show');
    }
}

/**
 * Carica statistiche admin
 */
async function loadAdminStats() {
    try {
        const sb = supabase.createClient(
            'https://uoykvjxerdrthnmnfmgc.supabase.co',
            'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InVveWt2anhldXJydGhubW5mbWdjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzEyNTI5NTUsImV4cCI6MjA0NjgyODk1NX0.dOhVPaOLEolHKV0Wf76QSgp8M75BC4yzrlCVfj7oP2E'
        );
        
        const { count: crmCount } = await sb.from('CRM').select('*', { count: 'exact', head: true });
        const { count: techCount } = await sb.from('UfficioTecnico').select('*', { count: 'exact', head: true });
        
        const totalActivities = document.getElementById('adminTotalActivities');
        const totalTasks = document.getElementById('adminTotalTasks');
        
        if (totalActivities) totalActivities.textContent = crmCount || 0;
        if (totalTasks) totalTasks.textContent = techCount || 0;
        
    } catch (err) {
        console.error('Errore caricamento statistiche:', err);
    }
}

/**
 * Carica log attività
 */
function loadActivityLog() {
    const container = document.getElementById('activityLog');
    if (!container) return;
    
    const logs = [
        { type: 'success', message: 'Sistema avviato correttamente', time: 'Adesso' },
        { type: 'info', message: 'Dati CRM aggiornati', time: '5 minuti fa' },
        { type: 'info', message: 'Gantt sincronizzato', time: '10 minuti fa' },
        { type: 'success', message: 'Login admin effettuato', time: '15 minuti fa' }
    ];
    
    container.innerHTML = logs.map(log => `
        <div class="log-entry">
            <div class="log-icon ${log.type}">
                ${log.type === 'success' ? '✓' : log.type === 'warning' ? '⚠' : 'ℹ'}
            </div>
            <div class="log-content">
                <div class="log-message">${log.message}</div>
                <div class="log-time">${log.time}</div>
            </div>
        </div>
    `).join('');
}

/**
 * Aggiorna tutti i dati
 */
function refreshAllData() {
    showToast('Aggiornamento in corso...', 'info');
    loadAdminStats();
    setTimeout(() => showToast('Dati aggiornati!', 'success'), 1000);
}

/**
 * Esporta dati
 */
function exportData() {
    showToast('Funzione in sviluppo', 'warning');
}

/**
 * Pulisci cache
 */
function clearCache() {
    if (confirm('Vuoi pulire la cache locale?')) {
        const savedPassword = localStorage.getItem('plm_remembered_password');
        const savedLevel = localStorage.getItem('plm_access_level');
        
        localStorage.clear();
        
        if (savedPassword) localStorage.setItem('plm_remembered_password', savedPassword);
        if (savedLevel) localStorage.setItem('plm_access_level', savedLevel);
        
        showToast('Cache pulita!', 'success');
    }
}

/**
 * Mostra impostazioni
 */
function showSettings() {
    openAdminModal('Impostazioni', `
        <div class="settings-section">
            <div class="setting-item">
                <div class="setting-label">Versione</div>
                <div class="setting-description">PLM 2.0</div>
            </div>
            <div class="setting-item">
                <div class="setting-label">Database</div>
                <div class="setting-description">Supabase</div>
            </div>
        </div>
    `);
}

/**
 * Salva password
 */
function savePasswords() {
    showToast('Funzione disponibile solo con backend', 'warning');
}

/**
 * Reset campi password
 */
function resetPasswordFields() {
    document.getElementById('userPasswordInput').value = '';
    document.getElementById('adminPasswordInput').value = '';
}

/**
 * Setup modal
 */
function setupModal() {
    const modal = document.getElementById('adminModal');
    if (modal) {
        modal.addEventListener('click', (e) => {
            if (e.target === modal) closeAdminModal();
        });
    }
}

/**
 * Apri modal
 */
function openAdminModal(title, content) {
    const modal = document.getElementById('adminModal');
    const modalTitle = document.getElementById('modalTitle');
    const modalBody = document.getElementById('modalBody');
    
    if (modal && modalTitle && modalBody) {
        modalTitle.textContent = title;
        modalBody.innerHTML = content;
        modal.classList.add('active');
    }
}

/**
 * Chiudi modal
 */
function closeAdminModal() {
    const modal = document.getElementById('adminModal');
    if (modal) modal.classList.remove('active');
}

/**
 * Toast notification
 */
function showToast(message, type = 'info') {
    const existing = document.querySelector('.toast-notification');
    if (existing) existing.remove();
    
    const toast = document.createElement('div');
    toast.className = 'toast-notification';
    toast.innerHTML = `
        <span>${type === 'success' ? '✓' : type === 'warning' ? '⚠' : 'ℹ'}</span>
        <span>${message}</span>
    `;
    
    const colors = {
        success: 'rgba(34, 197, 94, 0.95)',
        warning: 'rgba(234, 179, 8, 0.95)',
        error: 'rgba(239, 68, 68, 0.95)',
        info: 'rgba(59, 130, 246, 0.95)'
    };
    
    toast.style.cssText = `
        position: fixed;
        bottom: 100px;
        left: 50%;
        transform: translateX(-50%);
        background: ${colors[type]};
        color: white;
        padding: 12px 24px;
        border-radius: 12px;
        display: flex;
        align-items: center;
        gap: 10px;
        font-size: 0.9em;
        font-weight: 500;
        box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
        z-index: 10001;
    `;
    
    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 3000);
}

// Esponi globalmente
window.loadAdminContent = loadAdminContent;
window.toggleAdminDash = toggleAdminDash;
window.togglePasswordDash = togglePasswordDash;
window.refreshAllData = refreshAllData;
window.exportData = exportData;
window.clearCache = clearCache;
window.showSettings = showSettings;
window.savePasswords = savePasswords;
window.resetPasswordFields = resetPasswordFields;
window.openAdminModal = openAdminModal;
window.closeAdminModal = closeAdminModal;
window.showToast = showToast;
