/* ========================================
   LOGIN - PLM 2
   Sistema di autenticazione
   ======================================== */

const CORRECT_PASSWORD = 'Prosystem2025!';
const ADMIN_PASSWORD = 'ProsystemPro';
let userAccessLevel = 'user';

// Show loading animation on page load
window.addEventListener('DOMContentLoaded', function() {
    const savedPassword = localStorage.getItem('plm_remembered_password');
    const savedAccessLevel = localStorage.getItem('plm_access_level');
    
    if ((savedPassword === CORRECT_PASSWORD || savedPassword === ADMIN_PASSWORD) && savedAccessLevel) {
        userAccessLevel = savedAccessLevel;
        updateMenuVisibility();
        
        const loginOverlay = document.getElementById('loginOverlay');
        if (loginOverlay) {
            loginOverlay.style.display = 'none';
        }
        initializeDashboard();
        
    } else {
        setTimeout(() => {
            document.getElementById('loadingContainer').classList.add('hidden');
            setTimeout(() => {
                document.getElementById('loginFormContainer').classList.add('active');
                document.getElementById('passwordInput').focus();
            }, 300);
        }, 2000);
    }
});

function initializeDashboard() {
    // Carica i dati delle dashboard dopo il login
    // Home page
    if (typeof window.loadCRMData === 'function') {
        window.loadCRMData();
    }
    if (typeof window.loadGanttData === 'function') {
        window.loadGanttData();
    }
    // Amministrazione page
    if (typeof window.loadCastellettiData === 'function') {
        window.loadCastellettiData();
    }
}

function handleLogin(event) {
    event.preventDefault();
    
    const passwordInput = document.getElementById('passwordInput');
    const loginError = document.getElementById('loginError');
    const loginForm = document.getElementById('loginForm');
    const successCheck = document.getElementById('successCheck');
    const loginOverlay = document.getElementById('loginOverlay');
    const loginIcon = document.querySelector('.login-icon');
    const rememberPassword = document.getElementById('rememberPassword');
    
    const enteredPassword = passwordInput.value;
    
    if (enteredPassword === CORRECT_PASSWORD || enteredPassword === ADMIN_PASSWORD) {
        userAccessLevel = enteredPassword === ADMIN_PASSWORD ? 'admin' : 'user';
        
        if (rememberPassword.checked) {
            localStorage.setItem('plm_remembered_password', enteredPassword);
            localStorage.setItem('plm_access_level', userAccessLevel);
        } else {
            localStorage.removeItem('plm_remembered_password');
            localStorage.removeItem('plm_access_level');
        }
        
        updateMenuVisibility();
        
        loginError.classList.remove('show');
        loginForm.style.display = 'none';
        successCheck.classList.add('show');
        
        setTimeout(() => {
            void document.body.offsetHeight;
            
            const homeLogo = document.querySelector('.main-container .logo-container .logo');
            
            if (!homeLogo) {
                loginOverlay.classList.add('fade-out');
                setTimeout(() => {
                    loginOverlay.remove();
                    initializeDashboard();
                }, 800);
                return;
            }
            
            const homeLogoRect = homeLogo.getBoundingClientRect();
            const loginIconRect = loginIcon.getBoundingClientRect();
            
            const translateX = homeLogoRect.left - loginIconRect.left + (homeLogoRect.width - loginIconRect.width) / 2;
            const translateY = homeLogoRect.top - loginIconRect.top + (homeLogoRect.height - loginIconRect.height) / 2;
            const scale = homeLogoRect.width / loginIconRect.width;
            
            loginIcon.style.transform = `translate3d(${translateX}px, ${translateY}px, 0) scale(${scale})`;
            loginIcon.style.webkitTransform = `translate3d(${translateX}px, ${translateY}px, 0) scale(${scale})`;
            loginIcon.style.transition = 'transform 0.8s cubic-bezier(0.4, 0, 0.2, 1), -webkit-transform 0.8s cubic-bezier(0.4, 0, 0.2, 1)';
            
            setTimeout(() => {
                loginOverlay.classList.add('fade-out');
                
                setTimeout(() => {
                    loginOverlay.remove();
                    initializeDashboard();
                }, 800);
            }, 800);
        }, 1000);
        
    } else {
        loginError.classList.add('show');
        passwordInput.value = '';
        passwordInput.classList.add('shake');
        
        setTimeout(() => {
            passwordInput.classList.remove('shake');
            loginError.classList.remove('show');
        }, 2000);
    }
    
    return false;
}
