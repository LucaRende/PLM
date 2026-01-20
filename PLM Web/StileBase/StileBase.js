/* ========================================
   STILE BASE - PLM 2
   Carica Header, Footer, Menu da StileBase.html
   ======================================== */

/**
 * Carica tutti i componenti base (header, footer, menu, particles)
 */
async function loadBaseComponents() {
    const basePath = window.BASE_PATH;
    const filePath = basePath + '/StileBase/StileBase.html';
    
    try {
        const response = await fetch(filePath);
        const html = await response.text();
        
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');
        
        // Carica Particles (da template)
        const particlesTemplate = doc.getElementById('plm-particles');
        if (particlesTemplate) {
            const particlesDiv = document.createElement('div');
            particlesDiv.innerHTML = particlesTemplate.innerHTML;
            document.body.insertBefore(particlesDiv.firstElementChild, document.body.firstChild);
        }
        
        // Carica Header (da template)
        const headerTemplate = doc.getElementById('plm-header');
        const headerContainer = document.getElementById('header-container');
        if (headerTemplate && headerContainer) {
            headerContainer.innerHTML = headerTemplate.innerHTML;
            // Fix path logo
            const logo = headerContainer.querySelector('.logo');
            if (logo) logo.src = getLogoPath();
        }
        
        // Carica Footer (da template)
        const footerTemplate = doc.getElementById('plm-footer');
        const footerContainer = document.getElementById('footer-container');
        if (footerTemplate && footerContainer) {
            footerContainer.innerHTML = footerTemplate.innerHTML;
        }
        
        // Carica Menu (da template)
        const menuTemplate = doc.getElementById('plm-menu');
        const menuContainer = document.getElementById('menu-container');
        if (menuTemplate && menuContainer) {
            menuContainer.innerHTML = menuTemplate.innerHTML;
            fixMenuLinks();
            setupMenu();
        }
        
    } catch (error) {
        console.error('Errore caricamento componenti base:', error);
    }
}

/**
 * Ritorna il path corretto del logo in base alla pagina
 */
function getLogoPath() {
    if (window.PAGE_TYPE === 'admin') {
        return '../../logo-tondo.png';
    }
    return 'logo-tondo.png';
}

/**
 * Sistema i link del menu in base alla pagina corrente
 */
function fixMenuLinks() {
    const homeLink = document.querySelector('.menu-item[data-page="home"]');
    const adminLink = document.getElementById('adminMenuItem');
    
    if (window.PAGE_TYPE === 'home') {
        if (homeLink) homeLink.href = 'index.html';
        if (adminLink) adminLink.href = 'PLM Web/Amministrazione/Amministrazione.html';
    } else if (window.PAGE_TYPE === 'admin') {
        if (homeLink) homeLink.href = '../../index.html';
        if (adminLink) adminLink.href = 'Amministrazione.html';
    }
}

/**
 * Setup menu floating
 */
function setupMenu() {
    updateMenuVisibility();
    setCurrentPage();
}

/**
 * Toggle menu
 */
function toggleMenu() {
    const menuPanel = document.getElementById('menuPanel');
    const menuToggle = document.getElementById('menuToggle');
    
    if (menuPanel && menuToggle) {
        menuPanel.classList.toggle('active');
        menuToggle.classList.toggle('active');
    }
}

/**
 * Chiudi menu cliccando fuori
 */
document.addEventListener('click', function(e) {
    const menu = document.querySelector('.floating-menu');
    if (menu && !menu.contains(e.target)) {
        const menuPanel = document.getElementById('menuPanel');
        const menuToggle = document.getElementById('menuToggle');
        if (menuPanel) menuPanel.classList.remove('active');
        if (menuToggle) menuToggle.classList.remove('active');
    }
});

/**
 * Mostra/nascondi link admin nel menu
 */
function updateMenuVisibility() {
    const adminMenuItem = document.getElementById('adminMenuItem');
    if (adminMenuItem) {
        adminMenuItem.style.display = (window.userAccessLevel === 'admin') ? 'flex' : 'none';
    }
}

/**
 * Imposta pagina corrente nel menu
 */
function setCurrentPage() {
    const menuItems = document.querySelectorAll('.menu-item');
    menuItems.forEach(item => item.classList.remove('current'));
    
    if (window.PAGE_TYPE === 'home') {
        const homeItem = document.querySelector('.menu-item[data-page="home"]');
        if (homeItem) homeItem.classList.add('current');
    } else if (window.PAGE_TYPE === 'admin') {
        const adminItem = document.querySelector('.menu-item[data-page="admin"]');
        if (adminItem) adminItem.classList.add('current');
    }
}

/**
 * Scroll verso una dashboard
 */
function scrollToDashboard(headerId) {
    const header = document.getElementById(headerId);
    if (!header) return;
    
    if (!header.classList.contains('active')) {
        header.click();
        setTimeout(() => {
            header.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }, 350);
    } else {
        header.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
}

/**
 * Logout
 */
function logout() {
    localStorage.removeItem('plm_remembered_password');
    localStorage.removeItem('plm_access_level');
    window.userAccessLevel = null;
    location.reload();
}

/**
 * Aggiorna stats nel footer
 */
function updateFooterStats() {
    const activitiesCount = document.querySelectorAll('.activity-card').length;
    const tasksCount = document.getElementById('totalTasks')?.textContent || '-';
    
    const footerActivities = document.getElementById('footerActivities');
    const footerTasks = document.getElementById('footerTasks');
    
    if (footerActivities) footerActivities.textContent = activitiesCount || '-';
    if (footerTasks) footerTasks.textContent = tasksCount;
}

// Esponi globalmente
window.loadBaseComponents = loadBaseComponents;
window.toggleMenu = toggleMenu;
window.updateMenuVisibility = updateMenuVisibility;
window.scrollToDashboard = scrollToDashboard;
window.logout = logout;
window.updateFooterStats = updateFooterStats;
