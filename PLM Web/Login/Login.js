/* ========================================
   LOGIN - PLM 2
   Carica e gestisce la schermata di login
   ======================================== */

// Password (in produzione usare backend sicuro)
const PASSWORDS = {
    user: 'user',
    admin: 'admin'
};

/**
 * Carica il componente login
 */
async function loadLogin() {
    const container = document.getElementById('login-container');
    if (!container) return;
    
    const basePath = window.BASE_PATH;
    const filePath = basePath + '/Login/Login.html';
    
    try {
        const response = await fetch(filePath);
        const html = await response.text();
        
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');
        
        const template = doc.getElementById('plm-login');
        if (template) {
            container.innerHTML = template.innerHTML;
            setupLogin();
        }
    } catch (error) {
        console.error('Errore caricamento login:', error);
    }
}

/**
 * Setup iniziale login
 */
function setupLogin() {
    // Fix percorsi immagini
    const logoPath = getLoginLogoPath();
    const loaderLogo = document.getElementById('loaderLogo');
    const formLogo = document.getElementById('formLogo');
    if (loaderLogo) loaderLogo.src = logoPath;
    if (formLogo) formLogo.src = logoPath;
    
    // Imposta sottotitolo per admin
    if (window.PAGE_TYPE === 'admin') {
        const subtitle = document.getElementById('loginSubtitle');
        const button = document.getElementById('loginButton');
        if (subtitle) subtitle.textContent = 'Accesso Amministrazione';
        if (button) button.textContent = 'Accedi come Admin';
    }
    
    // Setup form
    const form = document.getElementById('loginForm');
    if (form) {
        form.addEventListener('submit', handleLogin);
    }
    
    // Controlla auto-login
    checkAutoLogin();
}

/**
 * Ritorna path logo per login
 */
function getLoginLogoPath() {
    if (window.PAGE_TYPE === 'admin') {
        return '../../logo-tondo.png';
    }
    return 'logo-tondo.png';
}

/**
 * Controlla se esiste password salvata
 */
function checkAutoLogin() {
    const savedPassword = localStorage.getItem('plm_remembered_password');
    const savedLevel = localStorage.getItem('plm_access_level');
    
    const loginLoader = document.getElementById('loginLoader');
    const loginFormContainer = document.getElementById('loginFormContainer');
    
    setTimeout(() => {
        if (loginLoader) loginLoader.classList.add('fade-out');
        
        setTimeout(() => {
            if (loginLoader) loginLoader.style.display = 'none';
            
            // Verifica auto-login
            if (savedPassword && validatePassword(savedPassword, savedLevel)) {
                // Auto-login valido
                window.userAccessLevel = savedLevel;
                onLoginSuccess();
            } else {
                // Mostra form
                if (loginFormContainer) loginFormContainer.style.opacity = '1';
            }
        }, 500);
    }, 1500);
}

/**
 * Valida password
 */
function validatePassword(password, requiredLevel = null) {
    if (window.PAGE_TYPE === 'admin') {
        // Admin richiede solo password admin
        return password === PASSWORDS.admin;
    }
    
    // Home accetta entrambe
    if (password === PASSWORDS.admin) {
        window.userAccessLevel = 'admin';
        return true;
    }
    if (password === PASSWORDS.user) {
        window.userAccessLevel = 'user';
        return true;
    }
    return false;
}

/**
 * Gestisce submit form login
 */
function handleLogin(event) {
    event.preventDefault();
    
    const passwordInput = document.getElementById('passwordInput');
    const loginError = document.getElementById('loginError');
    const enteredPassword = passwordInput?.value || '';
    
    if (validatePassword(enteredPassword)) {
        // Salva se richiesto
        const rememberPassword = document.getElementById('rememberPassword');
        if (rememberPassword && rememberPassword.checked) {
            localStorage.setItem('plm_remembered_password', enteredPassword);
            localStorage.setItem('plm_access_level', window.userAccessLevel);
        }
        
        // Success
        if (loginError) loginError.classList.remove('show');
        onLoginSuccess();
        
    } else {
        // Errore
        if (loginError) {
            if (window.PAGE_TYPE === 'admin') {
                loginError.textContent = '❌ Accesso negato - richiesta password admin';
            } else {
                loginError.textContent = '❌ Password non corretta';
            }
            loginError.classList.add('show');
        }
        
        if (passwordInput) {
            passwordInput.value = '';
            passwordInput.classList.add('shake');
            setTimeout(() => {
                passwordInput.classList.remove('shake');
                if (loginError) loginError.classList.remove('show');
            }, 2000);
        }
    }
    
    return false;
}

/**
 * Successo login - carica contenuto
 */
function onLoginSuccess() {
    const loginForm = document.getElementById('loginForm');
    const successCheck = document.getElementById('successCheck');
    const loginOverlay = document.getElementById('loginOverlay');
    const loginIcon = document.getElementById('loginIcon');
    
    if (loginForm) loginForm.style.display = 'none';
    if (successCheck) successCheck.classList.add('show');
    
    // Animazione icona verso centro
    if (loginIcon) {
        loginIcon.style.transition = 'all 0.8s cubic-bezier(0.4, 0, 0.2, 1)';
        loginIcon.style.transform = 'scale(1.2)';
    }
    
    setTimeout(() => {
        if (loginOverlay) {
            loginOverlay.classList.add('fade-out');
            
            setTimeout(() => {
                loginOverlay.remove();
                // Carica il contenuto della pagina
                loadPageContent();
            }, 800);
        }
    }, 1000);
}

/**
 * Carica contenuto pagina dopo login
 */
async function loadPageContent() {
    // Carica componenti base (header, footer, menu)
    await loadBaseComponents();
    
    // Carica contenuto specifico della pagina
    if (window.PAGE_TYPE === 'home') {
        await loadHomeContent();
    } else if (window.PAGE_TYPE === 'admin') {
        await loadAdminContent();
    }
    
    // Aggiorna visibilità menu admin
    updateMenuVisibility();
    
    // Aggiorna stats periodicamente
    setInterval(updateFooterStats, 5000);
}

/**
 * Controlla se utente è autenticato
 */
function isAuthenticated() {
    return window.userAccessLevel !== null;
}

/**
 * Ritorna livello accesso
 */
function getAccessLevel() {
    return window.userAccessLevel;
}

/**
 * Controlla se admin
 */
function isAdmin() {
    return window.userAccessLevel === 'admin';
}

// Esponi globalmente
window.loadLogin = loadLogin;
window.handleLogin = handleLogin;
window.isAuthenticated = isAuthenticated;
window.getAccessLevel = getAccessLevel;
window.isAdmin = isAdmin;
