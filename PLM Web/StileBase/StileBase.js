/* ========================================
   STILE BASE - PLM 2
   Funzioni globali condivise
   ======================================== */

// Menu floating
function toggleMenu() {
    const menuPanel = document.getElementById('menuPanel');
    const menuToggle = document.getElementById('menuToggle');
    
    if (menuPanel && menuToggle) {
        menuPanel.classList.toggle('active');
        menuToggle.classList.toggle('active');
    }
}

// Chiudi menu cliccando fuori
document.addEventListener('click', function(e) {
    const menu = document.querySelector('.floating-menu');
    if (menu && !menu.contains(e.target)) {
        const menuPanel = document.getElementById('menuPanel');
        const menuToggle = document.getElementById('menuToggle');
        if (menuPanel) menuPanel.classList.remove('active');
        if (menuToggle) menuToggle.classList.remove('active');
    }
});

// Aggiorna visibilità menu admin
function updateMenuVisibility() {
    const adminMenuItem = document.getElementById('adminMenuItem');
    if (adminMenuItem) {
        adminMenuItem.style.display = userAccessLevel === 'admin' ? 'flex' : 'none';
    }
}

// Scroll ai cruscotti
function scrollToDashboard(headerId) {
    const header = document.getElementById(headerId);
    if (!header) return;
    
    const isActive = header.classList.contains('active');
    if (!isActive) {
        header.click();
        setTimeout(() => {
            header.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }, 350);
    } else {
        header.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
}

// Logout
function logout() {
    localStorage.removeItem('plm_remembered_password');
    localStorage.removeItem('plm_access_level');
    location.reload();
}

// Update footer stats
function updateFooterStats() {
    const activitiesCount = document.querySelectorAll('.activity-card').length;
    const tasksCount = document.getElementById('totalTasks')?.textContent || '0';
    
    const footerActivities = document.getElementById('footerActivities');
    const footerTasks = document.getElementById('footerTasks');
    
    if (footerActivities) footerActivities.textContent = activitiesCount;
    if (footerTasks) footerTasks.textContent = tasksCount;
}

// Update stats periodicamente
setTimeout(updateFooterStats, 1000);
setInterval(updateFooterStats, 10000);

// Esponi globalmente
window.toggleMenu = toggleMenu;
window.updateMenuVisibility = updateMenuVisibility;
window.scrollToDashboard = scrollToDashboard;
window.logout = logout;
window.updateFooterStats = updateFooterStats;
