/* ========================================
   LOADER - PLM 2
   Funzioni utility per caricamento componenti
   ======================================== */

// Variabili globali
window.BASE_PATH = '';
window.PAGE_TYPE = '';
window.userAccessLevel = null;

/**
 * Carica un componente HTML da file
 */
async function loadComponent(filePath, containerId) {
    const container = document.getElementById(containerId);
    if (!container) return null;
    
    try {
        const response = await fetch(filePath);
        if (!response.ok) throw new Error('Errore caricamento: ' + filePath);
        const html = await response.text();
        container.innerHTML = html;
        return container;
    } catch (error) {
        console.error('Errore loadComponent:', error);
        return null;
    }
}

/**
 * Estrae una sezione specifica da un file HTML
 */
async function loadSection(filePath, sectionId, containerId) {
    const container = document.getElementById(containerId);
    if (!container) return null;
    
    try {
        const response = await fetch(filePath);
        if (!response.ok) throw new Error('Errore caricamento: ' + filePath);
        const html = await response.text();
        
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');
        const section = doc.getElementById(sectionId);
        
        if (section) {
            container.innerHTML = section.innerHTML;
            return container;
        }
        return null;
    } catch (error) {
        console.error('Errore loadSection:', error);
        return null;
    }
}

/**
 * Mostra loader principale
 */
function showLoader(message = 'Caricamento...') {
    let loader = document.getElementById('main-loader');
    if (!loader) {
        loader = document.createElement('div');
        loader.id = 'main-loader';
        loader.className = 'loading-overlay';
        loader.innerHTML = `
            <div class="loading-spinner"></div>
            <div class="loading-text">${message}</div>
        `;
        document.body.appendChild(loader);
    }
    loader.style.display = 'flex';
}

/**
 * Nascondi loader principale
 */
function hideLoader() {
    const loader = document.getElementById('main-loader');
    if (loader) {
        loader.classList.add('fade-out');
        setTimeout(() => loader.remove(), 500);
    }
}

/**
 * Inizializza l'applicazione
 */
async function initApp(pageType, basePath) {
    window.PAGE_TYPE = pageType;
    window.BASE_PATH = basePath;
    
    // 1. Carica Login
    await loadLogin();
}

// Esponi globalmente
window.loadComponent = loadComponent;
window.loadSection = loadSection;
window.showLoader = showLoader;
window.hideLoader = hideLoader;
window.initApp = initApp;
