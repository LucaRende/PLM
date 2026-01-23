// VARIABILI GLOBALI
// =============================================
let supabaseClient = null;
let currentUser = null;
let allBollettini = [];
let filteredBollettini = [];
let currentFilterType = 'miei';
let currentBollettinoId = null;
let isAdmin = false;
let isSuperAdmin = false;  // Solo superAdmin può eliminare bollettini
let notificheCount = 0;

// Filtri statistiche attivi (cliccando sui box)
let activeStatFilters = new Set();

// Permessi specifici
let canValidate = false;  // Può validare bollettini
let canInvoice = false;   // Può fatturare bollettini

// Canvas firma
let firmaCanvas = null;
let firmaCtx = null;
let isDrawing = false;
let hasSignature = false;

// Foto scontrini
let uploadedPhotos = [];

// Foto Prima/Dopo intervento
let uploadedFotoPrima = [];
let uploadedFotoDopo = [];

// DDT per fatturazione
let uploadedDDTs = [];

// Fatturazione Batch
let batchMode = false;
let selectedBollettiniIds = new Set();
let batchUploadedDDTs = [];

// Formatta ID bollettino #0001
function formatBollettinoId(id) {
    return '#' + String(id).padStart(4, '0');
}

// Email
let pendingEmailData = null;
let emailResolve = null;
let selectedAdminEmail = null;
let adminList = [];

// Logo per PDF
let logoBase64 = null;

// Precarica logo per PDF
function preloadLogo() {
    const img = new Image();
    img.crossOrigin = 'anonymous';
    img.onload = function() {
        const canvas = document.createElement('canvas');
        canvas.width = img.width;
        canvas.height = img.height;
        const ctx = canvas.getContext('2d');
        ctx.drawImage(img, 0, 0);
        logoBase64 = canvas.toDataURL('image/png');
        console.log('Logo precaricato per PDF');
    };
    img.onerror = function() {
        console.log('Logo non disponibile, uso placeholder');
    };
    img.src = '/PLM/logo-tondo.png';
}

// =============================================
// CONFIGURAZIONE EMAILJS
// =============================================
// IMPORTANTE: Sostituisci questi valori con i tuoi da emailjs.com
const EMAILJS_CONFIG = {
    publicKey: 'tCc5YReHVaUOz-Qeq',        // La tua Public Key da EmailJS
    serviceId: 'service_6oc4dph',        // Il tuo Service ID
    templateClienteId: 'template_bz6txdk',  // Template per email al cliente
    templateAdminId: 'template_bz6txdk',      // Template per notifica admin
    templateFatturazioneId: 'template_vx5mlmb' // Template per email fatturazione
};

// Inizializza EmailJS
function initEmailJS() {
    if (typeof emailjs !== 'undefined' && EMAILJS_CONFIG.publicKey !== 'YOUR_PUBLIC_KEY') {
        emailjs.init(EMAILJS_CONFIG.publicKey);
        console.log('EmailJS inizializzato');
    }
}

// =============================================
// SISTEMA NOTIFICHE
// =============================================
function countNotifiche() {
    const tecnicoCorrente = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim().toLowerCase();
    const tecnicoCorrenteAlt = `${currentUser.cognome || ''} ${currentUser.nome || ''}`.trim().toLowerCase();
    const cognome = (currentUser.cognome || '').toLowerCase();
    
    // Funzione per verificare se il bollettino appartiene all'utente
    const isMyBollettino = (b) => {
        const tecnicoDB = (b.tecnico_installatore || '').toLowerCase();
        return tecnicoDB === tecnicoCorrente || 
               tecnicoDB === tecnicoCorrenteAlt ||
               (cognome && tecnicoDB.includes(cognome));
    };
    
    if (isAdmin) {
        // Admin: conta bollettini DA VALIDARE (non validati e non eliminati)
        notificheCount = allBollettini.filter(b => 
            b.validato !== true && 
            b.eliminato !== true
        ).length;
    } else {
        // Utente normale: conta bollettini PROPRI VALIDATI che non ha ancora visto
        notificheCount = allBollettini.filter(b => 
            isMyBollettino(b) &&
            b.validato === true && 
            b.notifica_vista !== true
        ).length;
    }
    
    // Salva in sessionStorage per la home
    sessionStorage.setItem('bollettini_notifiche', notificheCount.toString());
    sessionStorage.setItem('bollettini_is_admin', isAdmin.toString());
    
    return notificheCount;
}

async function markAsViewed(bollettinoId) {
    const bollettino = allBollettini.find(b => b.id_bollettino === bollettinoId);
    if (!bollettino) return;
    
    const tecnicoCorrente = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim().toLowerCase();
    const tecnicoCorrenteAlt = `${currentUser.cognome || ''} ${currentUser.nome || ''}`.trim().toLowerCase();
    const cognome = (currentUser.cognome || '').toLowerCase();
    const tecnicoDB = (bollettino.tecnico_installatore || '').toLowerCase();
    
    const isMyBollettino = tecnicoDB === tecnicoCorrente || 
                           tecnicoDB === tecnicoCorrenteAlt ||
                           (cognome && tecnicoDB.includes(cognome));
    
    // Solo se è il proprio bollettino e è stato validato ma non ancora visto
    if (isMyBollettino && 
        bollettino.validato === true && 
        bollettino.notifica_vista !== true) {
        
        try {
            const { error } = await supabaseClient
                .from('BollettiniMontatori')
                .update({
                    notifica_vista: true,
                    data_visualizzazione: new Date().toISOString()
                })
                .eq('id_bollettino', bollettinoId);
            
            if (error) {
                console.log('Colonna notifica_vista non esiste ancora, ignoro');
                return;
            }
            
            // Aggiorna localmente
            bollettino.notifica_vista = true;
            bollettino.data_visualizzazione = new Date().toISOString();
            
            // Ricalcola notifiche e aggiorna UI
            countNotifiche();
            updateStats();
            renderBollettini();
            
        } catch (error) {
            console.error('Errore marcatura visualizzazione:', error);
        }
    }
}

// Funzione globale per ottenere notifiche (usata dalla home)
window.getBollettiniNotifiche = function() {
    return parseInt(sessionStorage.getItem('bollettini_notifiche') || '0');
};

window.isBollettiniAdmin = function() {
    return sessionStorage.getItem('bollettini_is_admin') === 'true';
};

// =============================================
// INIT
// =============================================
async function initSupabase() {
    try {
        const response = await fetch('/PLM/autenticazione.json');
        const config = await response.json();
        supabaseClient = supabase.createClient(config.supabase.url, config.supabase.anonKey);
        return true;
    } catch (error) {
        console.error('Errore Supabase:', error);
        return false;
    }
}

function loadUserData() {
    currentUser = JSON.parse(localStorage.getItem('plm_user') || sessionStorage.getItem('plm_user') || '{}');
    if (currentUser.cognome) document.getElementById('userSurname').textContent = currentUser.cognome;
    if (currentUser.nome) document.getElementById('userFirstName').textContent = currentUser.nome;
    
    // Check se è admin o manager
    const ruolo = (currentUser.ruolo || '').toLowerCase();
    isAdmin = ruolo === 'amministratore' || 
              ruolo === 'admin' || 
              ruolo === 'superadmin' ||
              ruolo === 'manager' ||
              currentUser.is_admin === true;
    
    // Check se è superAdmin (può eliminare bollettini)
    isSuperAdmin = ruolo === 'superadmin';
    
    // Carica permessi specifici dal localStorage (saranno aggiornati da loadUserPermissions)
    // NOTA: i permessi vengono SOLO dal campo specifico, NON dal ruolo admin
    canValidate = currentUser.puo_validare_bollettini === true;
    canInvoice = currentUser.puo_fatturare_bollettini === true;
    
    // Mostra/nascondi filtro operatore e bottone "tutti"
    if (isAdmin) {
        document.getElementById('filter-operatore-group').style.display = 'flex';
        document.getElementById('btn-tutti').style.display = 'block';
        // Mostra pulsante Multi per fatturazione batch
        if (canInvoice) {
            document.getElementById('btn-batch-mode').style.display = 'flex';
        }
        // Admin parte con "Tutti" di default
        currentFilterType = 'tutti';
        document.querySelector('.filter-toggle-btn[data-filter="miei"]').classList.remove('active');
        document.querySelector('.filter-toggle-btn[data-filter="tutti"]').classList.add('active');
    }
}

// Carica permessi utente dal database
async function loadUserPermissions() {
    if (!currentUser.id) return;
    
    try {
        const { data, error } = await supabaseClient
            .from('accounts')
            .select('puo_validare_bollettini, puo_fatturare_bollettini')
            .eq('id', currentUser.id)
            .single();
        
        if (data) {
            // I permessi vengono SOLO dal campo specifico nel DB
            canValidate = data.puo_validare_bollettini === true;
            canInvoice = data.puo_fatturare_bollettini === true;
            
            // Aggiorna localStorage per sessioni future
            currentUser.puo_validare_bollettini = data.puo_validare_bollettini;
            currentUser.puo_fatturare_bollettini = data.puo_fatturare_bollettini;
            localStorage.setItem('plm_user', JSON.stringify(currentUser));
            
            console.log('Permessi caricati - canValidate:', canValidate, 'canInvoice:', canInvoice);
        }
    } catch (e) {
        console.log('Errore caricamento permessi:', e);
    }
}

// =============================================
// FILTRI
// =============================================
function toggleFilters() {
    const panel = document.getElementById('filters-panel');
    const btn = document.getElementById('btn-expand-filters');
    
    panel.classList.toggle('open');
    btn.classList.toggle('active');
    
    // Aggiorna padding dopo la transizione
    setTimeout(updateContentPadding, 320);
}

function handleSearchInput() {
    const input = document.getElementById('search-input');
    const clearBtn = document.getElementById('btn-clear-search');
    
    // Mostra/nascondi pulsante clear
    clearBtn.classList.toggle('show', input.value.length > 0);
    
    applyFilters();
    updateActiveFiltersChips();
}

function clearSearch() {
    document.getElementById('search-input').value = '';
    document.getElementById('btn-clear-search').classList.remove('show');
    applyFilters();
    updateActiveFiltersChips();
}

function setFilterType(type) {
    // Solo admin può vedere tutti
    if (type === 'tutti' && !isAdmin) return;
    
    currentFilterType = type;
    
    document.querySelectorAll('.filter-toggle-btn').forEach(btn => {
        btn.classList.toggle('active', btn.dataset.filter === type);
    });
    
    applyFilters();
    updateActiveFiltersChips();
}

function resetFilters() {
    document.getElementById('search-input').value = '';
    document.getElementById('btn-clear-search').classList.remove('show');
    
    const filterOp = document.getElementById('filter-operatore');
    if (filterOp) filterOp.value = '';
    
    const filterCl = document.getElementById('filter-cliente');
    if (filterCl) filterCl.value = '';
    
    const filterDurMin = document.getElementById('filter-durata-min');
    if (filterDurMin) filterDurMin.value = '';
    
    const filterDurMax = document.getElementById('filter-durata-max');
    if (filterDurMax) filterDurMax.value = '';
    
    const filterDataDa = document.getElementById('filter-data-da');
    if (filterDataDa) filterDataDa.value = '';
    
    const filterDataA = document.getElementById('filter-data-a');
    if (filterDataA) filterDataA.value = '';
    
    setFilterType('miei');
    updateFilterBadge();
    updateActiveFiltersChips();
}

function clearSingleFilter(filterName) {
    const el = document.getElementById(filterName);
    if (el) {
        el.value = '';
        applyFilters();
        updateActiveFiltersChips();
    }
}

function updateActiveFiltersChips() {
    const container = document.getElementById('active-filters');
    if (!container) return; // Container non esiste nell'HTML attuale
    
    let chips = [];
    
    const search = document.getElementById('search-input');
    if (search && search.value) {
        chips.push(`<span class="filter-chip">Cerca: "${search.value}" <button onclick="clearSearch()"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button></span>`);
    }
    
    const cliente = document.getElementById('filter-cliente');
    if (cliente && cliente.value) {
        chips.push(`<span class="filter-chip">Cliente: ${cliente.value} <button onclick="clearSingleFilter('filter-cliente')"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button></span>`);
    }
    
    container.innerHTML = chips.join('');
    
    // Aggiorna padding del contenuto in base ai chips attivi
    // Aggiorna padding dopo che i chips sono renderizzati
    setTimeout(updateContentPadding, 50);
}

function updateContentPadding() {
    const content = document.getElementById('page-content');
    const filtersSection = document.querySelector('.search-filters-section');
    
    if (!content || !filtersSection) return;
    
    // Calcola altezza totale della sezione fixed (106px header + title bar)
    const baseTop = 106;
    const filtersSectionHeight = filtersSection.offsetHeight;
    
    // Padding = base + altezza sezione filtri + margine
    let paddingTop = baseTop + filtersSectionHeight + 12;
    
    content.style.paddingTop = paddingTop + 'px';
}

function applyFilters() {
    const search = document.getElementById('search-input').value.toLowerCase().trim();
    const filterOp = document.getElementById('filter-operatore');
    const operatore = filterOp ? filterOp.value : '';
    const filterCl = document.getElementById('filter-cliente');
    const cliente = filterCl ? filterCl.value.toLowerCase().trim() : '';
    const filterDurMin = document.getElementById('filter-durata-min');
    const durataMin = filterDurMin ? (parseFloat(filterDurMin.value) || 0) : 0;
    const filterDurMax = document.getElementById('filter-durata-max');
    const durataMax = filterDurMax ? (parseFloat(filterDurMax.value) || 999) : 999;
    const filterDataDa = document.getElementById('filter-data-da');
    const dataDa = filterDataDa ? filterDataDa.value : '';
    const filterDataA = document.getElementById('filter-data-a');
    const dataA = filterDataA ? filterDataA.value : '';
    
    const tecnicoCorrente = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim();
    const tecnicoCorrenteAlt = `${currentUser.cognome || ''} ${currentUser.nome || ''}`.trim();
    
    filteredBollettini = allBollettini.filter(b => {
        // Filtro miei/tutti
        if (currentFilterType === 'miei') {
            const tecnicoDB = (b.tecnico_installatore || '').toLowerCase();
            const match = tecnicoDB === tecnicoCorrente.toLowerCase() || 
                          tecnicoDB === tecnicoCorrenteAlt.toLowerCase() ||
                          tecnicoDB.includes(currentUser.cognome?.toLowerCase() || '---');
            if (!match) return false;
        }
        
        // Ricerca testuale
        if (search) {
            const searchIn = [
                b.cliente || '',
                b.montaggio_macchina || '',
                b.lavori_eseguiti || '',
                b.note || '',
                b.tecnico_installatore || ''
            ].join(' ').toLowerCase();
            if (!searchIn.includes(search)) return false;
        }
        
        // Filtro operatore (solo admin)
        if (operatore && b.tecnico_installatore !== operatore) return false;
        
        // Filtro cliente (ricerca parziale - è un input text)
        if (cliente && !(b.cliente || '').toLowerCase().includes(cliente)) return false;
        
        // Filtro durata
        const ore = parseFloat(b.ore_totali) || 0;
        if (ore < durataMin || ore > durataMax) return false;
        
        // Filtro date
        if (dataDa && b.data < dataDa) return false;
        if (dataA && b.data > dataA) return false;
        
        // ========================================
        // FILTRI STATISTICHE (box cliccabili)
        // ========================================
        if (activeStatFilters.size > 0) {
            const hasFirma = b.firma_cliente && b.firma_cliente.data;
            const isValidato = b.validato === true;
            const isFatturato = b.fatturato === true;
            const isDaFatturare = isValidato && !isFatturato;
            
            // Ogni filtro attivo deve essere soddisfatto (AND logic)
            if (activeStatFilters.has('firmati') && !hasFirma) return false;
            if (activeStatFilters.has('validati') && !isValidato) return false;
            if (activeStatFilters.has('fatturati') && !isFatturato) return false;
            if (activeStatFilters.has('da-fatturare') && !isDaFatturare) return false;
        }
        
        return true;
    });
    
    renderBollettini();
    updateStats();
    updateFilterBadge();
    updateActiveFiltersChips();
    updateStatFiltersUI();
}

// =============================================
// FILTRI STATISTICHE (box cliccabili)
// =============================================
function toggleStatFilter(filterType) {
    // Totali e Ore non sono filtri, mostrano solo le statistiche
    if (filterType === 'totali' || filterType === 'ore') {
        return;
    }
    
    // Toggle del filtro
    if (activeStatFilters.has(filterType)) {
        activeStatFilters.delete(filterType);
    } else {
        activeStatFilters.add(filterType);
    }
    
    // Riapplica filtri
    applyFilters();
}

function clearStatFilters() {
    activeStatFilters.clear();
    applyFilters();
}

function updateStatFiltersUI() {
    // Aggiorna classi active sui box
    document.querySelectorAll('.stat-chip.clickable').forEach(chip => {
        const filter = chip.dataset.filter;
        if (activeStatFilters.has(filter)) {
            chip.classList.add('active');
        } else {
            chip.classList.remove('active');
        }
    });
    
    // Mostra/nascondi barra filtri attivi (potrebbe non esistere)
    const filtersBar = document.getElementById('stat-filters-active');
    const chipsContainer = document.getElementById('stat-filters-chips');
    
    if (!filtersBar || !chipsContainer) return;
    
    if (activeStatFilters.size > 0) {
        filtersBar.style.display = 'flex';
        
        // Genera chips per ogni filtro attivo
        const filterLabels = {
            'firmati': '✍️ Firmati',
            'validati': '✅ Validati',
            'da-fatturare': '⏳ Da Fatturare',
            'fatturati': '📦 Fatturati'
        };
        
        chipsContainer.innerHTML = Array.from(activeStatFilters).map(f => `
            <span class="stat-filter-chip" onclick="toggleStatFilter('${f}')">
                ${filterLabels[f] || f} ✕
            </span>
        `).join('');
    } else {
        filtersBar.style.display = 'none';
        chipsContainer.innerHTML = '';
    }
}

function updateFilterBadge() {
    let count = 0;
    
    // Non contare la ricerca nel badge, è già visibile
    const filterOp = document.getElementById('filter-operatore');
    if (filterOp && filterOp.value) count++;
    
    const filterCl = document.getElementById('filter-cliente');
    if (filterCl && filterCl.value) count++;
    
    const filterDurMin = document.getElementById('filter-durata-min');
    if (filterDurMin && filterDurMin.value) count++;
    
    const filterDurMax = document.getElementById('filter-durata-max');
    if (filterDurMax && filterDurMax.value) count++;
    
    const filterDataDa = document.getElementById('filter-data-da');
    if (filterDataDa && filterDataDa.value) count++;
    
    const filterDataA = document.getElementById('filter-data-a');
    if (filterDataA && filterDataA.value) count++;
    
    const badge = document.getElementById('filter-badge');
    if (badge) {
        badge.textContent = count;
        badge.classList.toggle('show', count > 0);
    }
}

// =============================================
// CARICA BOLLETTINI
// =============================================
async function loadBollettini(showLoader = true) {
    try {
        if (showLoader) {
            Loader.show('funzione', 'Caricamento bollettini...');
        }
        
        // Query semplice senza filtri complicati
        const { data, error } = await supabaseClient
            .from('BollettiniMontatori')
            .select('*')
            .order('data', { ascending: false });
        
        if (error) throw error;
        
        // Filtra lato client i bollettini eliminati (se la colonna esiste)
        allBollettini = (data || []).filter(b => b.eliminato !== true);
        
        // Conta notifiche
        countNotifiche();
        
        // Popola filtri dropdown
        populateFilterDropdowns();
        
        // Applica filtri iniziali
        applyFilters();
        
        // ASPETTA che il browser abbia renderizzato le card
        await new Promise(resolve => {
            requestAnimationFrame(() => {
                requestAnimationFrame(resolve);
            });
        });
        
    } catch (error) {
        console.error('Errore caricamento:', error);
        const listEl = document.getElementById('bollettini-list');
        if (listEl) {
            listEl.innerHTML = `
                <div class="empty-state">
                    <div class="empty-state-icon">❌</div>
                    <p class="empty-state-text">Errore nel caricamento: ${error.message}</p>
                </div>
            `;
        }
    } finally {
        if (showLoader) {
            Loader.hide();
        }
    }
}

function populateFilterDropdowns() {
    // Operatori (unici) - questo è un SELECT
    const operatori = [...new Set(allBollettini.map(b => b.tecnico_installatore).filter(Boolean))].sort();
    const selectOp = document.getElementById('filter-operatore');
    if (selectOp) {
        selectOp.innerHTML = '<option value="">Tutti</option>' + 
            operatori.map(o => `<option value="${o}">${o}</option>`).join('');
    }
    
    // filter-cliente è un INPUT TEXT, non un SELECT - non va popolato
    // filter-macchina e filter-stato non esistono nell'HTML attuale
}

function updateStats() {
    const totali = filteredBollettini.length;
    const oreTotali = filteredBollettini.reduce((sum, b) => sum + (parseFloat(b.ore_totali) || 0), 0);
    const firmati = filteredBollettini.filter(b => b.firma_cliente && b.firma_cliente.data).length;
    const validati = filteredBollettini.filter(b => b.validato === true).length;
    const fatturati = filteredBollettini.filter(b => b.fatturato === true).length;
    const daFatturare = filteredBollettini.filter(b => b.validato === true && b.fatturato !== true).length;
    
    // Null check su tutti gli elementi (alcuni potrebbero non esistere nell'HTML)
    const elTotali = document.getElementById('stat-totali');
    if (elTotali) elTotali.textContent = totali;
    
    const elOre = document.getElementById('stat-ore');
    if (elOre) elOre.textContent = oreTotali.toFixed(1) + 'h';
    
    const elFirmati = document.getElementById('stat-firmati');
    if (elFirmati) elFirmati.textContent = firmati;
    
    const elValidati = document.getElementById('stat-validati');
    if (elValidati) elValidati.textContent = validati;
    
    const elDaFatturare = document.getElementById('stat-da-fatturare');
    if (elDaFatturare) elDaFatturare.textContent = daFatturare;
    
    const elFatturati = document.getElementById('stat-fatturati');
    if (elFatturati) elFatturati.textContent = fatturati;
    
    // Aggiorna stat notifiche
    const notificheChip = document.getElementById('stat-notifiche-chip');
    const notificheValue = document.getElementById('stat-notifiche');
    const notificheLabel = document.getElementById('stat-notifiche-label');
    
    if (notificheChip && notificheCount > 0) {
        notificheChip.style.display = 'flex';
        if (notificheValue) notificheValue.textContent = notificheCount;
        
        if (isAdmin) {
            notificheChip.classList.add('admin');
            notificheChip.classList.remove('user');
            if (notificheLabel) notificheLabel.textContent = 'Da validare';
        } else {
            notificheChip.classList.add('user');
            notificheChip.classList.remove('admin');
            if (notificheLabel) notificheLabel.textContent = 'Nuovi';
        }
    } else if (notificheChip) {
        notificheChip.style.display = 'none';
    }
}

// =============================================
// RENDER BOLLETTINI
// =============================================
function renderBollettini() {
    const container = document.getElementById('bollettini-list');
    if (!container) return;
    
    if (filteredBollettini.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <svg class="empty-state-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <polyline points="14 2 14 8 20 8"/>
                    <line x1="16" y1="13" x2="8" y2="13"/>
                    <line x1="16" y1="17" x2="8" y2="17"/>
                    <polyline points="10 9 9 9 8 9"/>
                </svg>
                <p class="empty-state-text">Nessun bollettino trovato</p>
                <button class="btn btn-primary" onclick="openModal(true)">Crea il primo</button>
            </div>
        `;
        return;
    }
    
    // Genera HTML
    let html = '';
    filteredBollettini.forEach(b => {
        const dataObj = b.data ? new Date(b.data) : null;
        const dateStr = dataObj ? dataObj.toLocaleDateString('it-IT', { day: '2-digit', month: 'short' }) : '-';
        const weekday = dataObj ? dataObj.toLocaleDateString('it-IT', { weekday: 'short' }) : '';
        const isValidated = b.validato === true;
        const isInvoiced = b.fatturato === true;
        const hasFirma = b.firma_cliente && b.firma_cliente.data;
        
        let badge = '<span class="bollettino-badge pending">In attesa</span>';
        if (isInvoiced) {
            badge = '<span class="bollettino-badge completed">✓ Completo</span>';
        } else if (isValidated) {
            badge = '<span class="bollettino-badge validated">✓ Validato</span>';
        } else if (hasFirma) {
            badge = '<span class="bollettino-badge signed">Firmato</span>';
        }
        
        html += `
            <div class="bollettino-card" data-id="${b.id_bollettino}" onclick="openDettaglio(${b.id_bollettino})">
                <div class="card-checkbox" onclick="toggleBollettinoSelection(${b.id_bollettino}, event)">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
                        <polyline points="20 6 9 17 4 12"/>
                    </svg>
                </div>
                <div class="bollettino-card-header">
                    <div class="bollettino-date-block">
                        <div class="bollettino-date">${dateStr}</div>
                        <div class="bollettino-weekday">${weekday}</div>
                    </div>
                    ${badge}
                </div>
                <div class="bollettino-card-body">
                    <div class="bollettino-cliente">${b.cliente || 'Cliente non specificato'}</div>
                    <div class="bollettino-macchina">${b.montaggio_macchina || 'Macchina non specificata'}${b.matricola ? ` <span class="bollettino-matricola">(${b.matricola})</span>` : ''}</div>
                </div>
                <div class="bollettino-card-footer">
                    <div class="bollettino-meta">
                        <div class="bollettino-ore">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="12" cy="12" r="10"/>
                                <polyline points="12 6 12 12 16 14"/>
                            </svg>
                            ${b.ore_totali || 0}h
                        </div>
                        <div class="bollettino-orario">${b.orario_inizio || '--:--'} - ${b.orario_fine || '--:--'}</div>
                    </div>
                    <div class="bollettino-tecnico">${b.tecnico_installatore || ''}</div>
                </div>
            </div>
        `;
    });
    
    container.innerHTML = html;
}

// =============================================
// MODAL CREA/MODIFICA
// =============================================
function openModal(isNew = true) {
    const modal = document.getElementById('modal-bollettino');
    
    if (isNew) {
        document.getElementById('modal-title').textContent = 'Nuovo Bollettino';
        document.getElementById('bollettino-id').value = '';
        document.getElementById('bollettino-data').value = new Date().toISOString().split('T')[0];
        
        // Orario inizio: 08:00 di default
        document.getElementById('bollettino-orario-inizio').value = '08:00';
        
        // Orario fine: ora attuale
        const now = new Date();
        const oraAttuale = now.getHours().toString().padStart(2, '0') + ':' + now.getMinutes().toString().padStart(2, '0');
        document.getElementById('bollettino-orario-fine').value = oraAttuale;
        
        document.getElementById('bollettino-pausa').value = '60';
        document.getElementById('bollettino-cliente').value = '';
        document.getElementById('bollettino-macchina').value = '';
        document.getElementById('bollettino-matricola').value = '';
        document.getElementById('bollettino-tecnico').value = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim();
        document.getElementById('bollettino-lavori').value = '';
        document.getElementById('bollettino-note').value = '';
        document.getElementById('bollettino-totale-speso').value = '';
        document.getElementById('bollettino-email').value = '';
        document.getElementById('bollettino-firmatario').value = '';
        
        uploadedPhotos = [];
        renderPhotosGrid();
        
        // Reset foto prima/dopo
        uploadedFotoPrima = [];
        uploadedFotoDopo = [];
        renderFotoPrimaGrid();
        renderFotoDopoGrid();
        
        // Calcola ore con i valori di default
        setTimeout(calcOre, 100);
    }
    
    initFirmaCanvas();
    
    modal.classList.add('show');
    document.body.style.overflow = 'hidden';
}

function closeModal() {
    document.getElementById('modal-bollettino').classList.remove('show');
    document.body.style.overflow = '';
}

// =============================================
// CALCOLO ORE
// =============================================
function calcOre() {
    const inizio = document.getElementById('bollettino-orario-inizio').value;
    const fine = document.getElementById('bollettino-orario-fine').value;
    const pausa = parseInt(document.getElementById('bollettino-pausa').value) || 0;
    
    if (!inizio || !fine) {
        document.getElementById('bollettino-ore').textContent = '0.00 h';
        return;
    }
    
    const [hi, mi] = inizio.split(':').map(Number);
    const [hf, mf] = fine.split(':').map(Number);
    
    let minTotali = (hf * 60 + mf) - (hi * 60 + mi) - pausa;
    if (minTotali < 0) minTotali = 0;
    
    const ore = (minTotali / 60).toFixed(2);
    document.getElementById('bollettino-ore').textContent = `${ore} h`;
}

// =============================================
// CANVAS FIRMA
// =============================================
function initFirmaCanvas() {
    firmaCanvas = document.getElementById('firma-canvas');
    firmaCtx = firmaCanvas.getContext('2d');
    
    const wrapper = firmaCanvas.parentElement;
    firmaCanvas.width = wrapper.offsetWidth;
    firmaCanvas.height = 120;
    
    // Sfondo BIANCO per il canvas (così il PNG esportato ha sfondo bianco)
    firmaCtx.fillStyle = '#ffffff';
    firmaCtx.fillRect(0, 0, firmaCanvas.width, firmaCanvas.height);
    
    firmaCtx.strokeStyle = '#1a1a1a';
    firmaCtx.lineWidth = 2;
    firmaCtx.lineCap = 'round';
    firmaCtx.lineJoin = 'round';
    
    isDrawing = false;
    hasSignature = false;
    wrapper.classList.remove('has-signature');
    
    // Touch events
    firmaCanvas.addEventListener('touchstart', handleTouchStart, { passive: false });
    firmaCanvas.addEventListener('touchmove', handleTouchMove, { passive: false });
    firmaCanvas.addEventListener('touchend', handleTouchEnd);
    
    // Mouse events
    firmaCanvas.addEventListener('mousedown', handleMouseDown);
    firmaCanvas.addEventListener('mousemove', handleMouseMove);
    firmaCanvas.addEventListener('mouseup', handleMouseUp);
    firmaCanvas.addEventListener('mouseleave', handleMouseUp);
}

function getPos(e) {
    const rect = firmaCanvas.getBoundingClientRect();
    const touch = e.touches ? e.touches[0] : e;
    return {
        x: touch.clientX - rect.left,
        y: touch.clientY - rect.top
    };
}

function handleTouchStart(e) {
    e.preventDefault();
    const pos = getPos(e);
    firmaCtx.beginPath();
    firmaCtx.moveTo(pos.x, pos.y);
    isDrawing = true;
}

function handleTouchMove(e) {
    if (!isDrawing) return;
    e.preventDefault();
    const pos = getPos(e);
    firmaCtx.lineTo(pos.x, pos.y);
    firmaCtx.stroke();
    hasSignature = true;
    document.getElementById('firma-wrapper').classList.add('has-signature');
}

function handleTouchEnd() {
    isDrawing = false;
}

function handleMouseDown(e) {
    const pos = getPos(e);
    firmaCtx.beginPath();
    firmaCtx.moveTo(pos.x, pos.y);
    isDrawing = true;
}

function handleMouseMove(e) {
    if (!isDrawing) return;
    const pos = getPos(e);
    firmaCtx.lineTo(pos.x, pos.y);
    firmaCtx.stroke();
    hasSignature = true;
    document.getElementById('firma-wrapper').classList.add('has-signature');
}

function handleMouseUp() {
    isDrawing = false;
}

function clearFirma() {
    if (firmaCtx && firmaCanvas) {
        firmaCtx.clearRect(0, 0, firmaCanvas.width, firmaCanvas.height);
        // Rimetti sfondo bianco
        firmaCtx.fillStyle = '#ffffff';
        firmaCtx.fillRect(0, 0, firmaCanvas.width, firmaCanvas.height);
        hasSignature = false;
        document.getElementById('firma-wrapper').classList.remove('has-signature');
    }
}

function getFirmaData() {
    if (!hasSignature || !firmaCanvas) return null;
    return {
        data: firmaCanvas.toDataURL('image/png'),
        timestamp: new Date().toISOString()
    };
}

function loadFirmaToCanvas(firmaObj) {
    if (!firmaObj || !firmaObj.data) return;
    
    const img = new Image();
    img.onload = function() {
        if (firmaCtx) {
            // Prima metti sfondo bianco
            firmaCtx.fillStyle = '#ffffff';
            firmaCtx.fillRect(0, 0, firmaCanvas.width, firmaCanvas.height);
            // Poi disegna la firma
            firmaCtx.drawImage(img, 0, 0, firmaCanvas.width, firmaCanvas.height);
            hasSignature = true;
            document.getElementById('firma-wrapper').classList.add('has-signature');
        }
    };
    img.src = firmaObj.data;
}

// =============================================
// FOTO SCONTRINI
// =============================================
function handlePhotoUpload(event) {
    const files = event.target.files;
    if (!files || files.length === 0) return;
    
    Array.from(files).forEach(file => {
        if (file.type.startsWith('image/')) {
            const reader = new FileReader();
            reader.onload = function(e) {
                uploadedPhotos.push({
                    data: e.target.result,
                    name: file.name,
                    timestamp: new Date().toISOString()
                });
                renderPhotosGrid();
            };
            reader.readAsDataURL(file);
        }
    });
    
    // Reset input
    event.target.value = '';
}

function removePhoto(index) {
    uploadedPhotos.splice(index, 1);
    renderPhotosGrid();
}

function renderPhotosGrid() {
    const grid = document.getElementById('photos-grid');
    
    let html = uploadedPhotos.map((photo, index) => `
        <div class="photo-item">
            <img src="${photo.data}" alt="Scontrino">
            <button class="photo-remove" onclick="event.stopPropagation(); removePhoto(${index})">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="18" y1="6" x2="6" y2="18"/>
                    <line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
            </button>
        </div>
    `).join('');
    
    // Aggiungi placeholder per aggiungere
    html += `
        <div class="photo-item photo-add-placeholder" onclick="document.getElementById('scontrini-input').click()">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="3" width="18" height="18" rx="2" ry="2"/>
                <line x1="12" y1="8" x2="12" y2="16"/>
                <line x1="8" y1="12" x2="16" y2="12"/>
            </svg>
            <span>Aggiungi</span>
        </div>
    `;
    
    grid.innerHTML = html;
}

// =============================================
// SALVA BOLLETTINO
// =============================================
async function saveBollettino() {
    const id = document.getElementById('bollettino-id').value;
    
    const oreText = document.getElementById('bollettino-ore').textContent;
    const oreValue = parseFloat(oreText.replace(' h', '').replace(',', '.')) || 0;
    
    const data = {
        data: document.getElementById('bollettino-data').value || null,
        orario_inizio: document.getElementById('bollettino-orario-inizio').value || null,
        orario_fine: document.getElementById('bollettino-orario-fine').value || null,
        pausa_minuti: parseInt(document.getElementById('bollettino-pausa').value) || 0,
        ore_totali: oreValue,
        cliente: document.getElementById('bollettino-cliente').value || null,
        montaggio_macchina: document.getElementById('bollettino-macchina').value || null,
        matricola: document.getElementById('bollettino-matricola').value || null,
        tecnico_installatore: document.getElementById('bollettino-tecnico').value || null,
        lavori_eseguiti: document.getElementById('bollettino-lavori').value || null,
        note: document.getElementById('bollettino-note').value || null,
        firma_cliente: getFirmaData(),
        foto_scontrini: uploadedPhotos.length > 0 ? uploadedPhotos : null,
        foto_prima: uploadedFotoPrima.length > 0 ? uploadedFotoPrima : null,
        foto_dopo: uploadedFotoDopo.length > 0 ? uploadedFotoDopo : null,
        totale_speso: parseFloat(document.getElementById('bollettino-totale-speso').value) || null,
        email_cliente: document.getElementById('bollettino-email').value || null,
        nome_firmatario: document.getElementById('bollettino-firmatario').value || null,
        data_modifica: new Date().toISOString()
    };
    
    // Validazione
    if (!data.data || !data.cliente) {
        alert('Data e Cliente sono obbligatori');
        return;
    }
    
    try {
        Loader.show('invio', 'Salvataggio...');
        
        let result;
        
        if (id) {
            const { data: updated, error } = await supabaseClient
                .from('BollettiniMontatori')
                .update(data)
                .eq('id_bollettino', id)
                .select();
            
            if (error) throw error;
            result = updated;
        } else {
            data.data_creazione = new Date().toISOString();
            const { data: inserted, error } = await supabaseClient
                .from('BollettiniMontatori')
                .insert([data])
                .select();
            
            if (error) throw error;
            result = inserted;
        }
        
        closeModal();
        
        // Aggiungi l'id_bollettino al data per l'email
        if (result && result[0] && result[0].id_bollettino) {
            data.id_bollettino = result[0].id_bollettino;
        } else if (id) {
            data.id_bollettino = parseInt(id);
        }
        
        // Aggiorna testo loader
        Loader.updateText('Aggiornamento lista...');
        
        // Ricarica senza loader interno (usa quello già attivo)
        await loadBollettini(false);
        
        // Aspetta rendering
        await new Promise(resolve => {
            requestAnimationFrame(() => {
                requestAnimationFrame(resolve);
            });
        });
        
        // Nascondi loader prima di chiedere per email
        Loader.hide();
        
        // Se c'è firma ed email, chiedi se inviare copia al cliente
        const hasFirma = data.firma_cliente && data.firma_cliente.data;
        const hasEmail = data.email_cliente && data.email_cliente.trim();
        
        if (hasFirma && hasEmail) {
            const shouldSend = await askSendEmailToCliente(data.email_cliente, data);
            
            if (shouldSend) {
                Loader.show('invio', 'Invio email...');
                const emailSent = await sendEmailToCliente(data);
                Loader.hide();
                
                if (emailSent) {
                    // Mostra conferma breve
                    alert('✅ Email inviata con successo!');
                } else {
                    alert('⚠️ Impossibile inviare l\'email. EmailJS non configurato o errore di rete.');
                }
            }
        }
        
        return; // Esce dalla funzione, già gestito tutto
        
    } catch (error) {
        console.error('Errore salvataggio:', error);
        alert('Errore durante il salvataggio: ' + error.message);
    } finally {
        Loader.hide();
    }
}

// =============================================
// MODAL DETTAGLIO
// =============================================
function openDettaglio(id) {
    // Se siamo in batch mode, gestisci selezione invece di aprire dettaglio
    if (batchMode) {
        const bollettino = allBollettini.find(b => b.id_bollettino === id);
        if (bollettino && bollettino.validato && !bollettino.fatturato) {
            // È selezionabile - toggle selezione
            toggleBollettinoSelection(id);
            return;
        }
        // Non è selezionabile (non validato o già fatturato) - non fare nulla o apri dettaglio
        // Apriamo comunque il dettaglio per i non selezionabili
    }
    
    currentBollettinoId = id;
    const bollettino = allBollettini.find(b => b.id_bollettino === id);
    if (!bollettino) return;
    
    const dataFormatted = bollettino.data ? 
        new Date(bollettino.data).toLocaleDateString('it-IT', { 
            weekday: 'long', day: '2-digit', month: 'long', year: 'numeric' 
        }) : '-';
    
    const hasFirma = bollettino.firma_cliente && bollettino.firma_cliente.data;
    const hasPhotos = bollettino.foto_scontrini && bollettino.foto_scontrini.length > 0;
    const isValidated = bollettino.validato === true;
    const isInvoiced = bollettino.fatturato === true;
    const hasDDT = bollettino.ddt_files && bollettino.ddt_files.length > 0;
    
    // Check se è una notifica nuova (validato ma non visto) - confronto flessibile
    const tecnicoCorrente = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim().toLowerCase();
    const tecnicoCorrenteAlt = `${currentUser.cognome || ''} ${currentUser.nome || ''}`.trim().toLowerCase();
    const cognome = (currentUser.cognome || '').toLowerCase();
    const tecnicoDB = (bollettino.tecnico_installatore || '').toLowerCase();
    const isMyBollettino = tecnicoDB === tecnicoCorrente || 
                           tecnicoDB === tecnicoCorrenteAlt ||
                           (cognome && tecnicoDB.includes(cognome));
    
    const isNewNotification = isMyBollettino && 
                              isValidated && 
                              bollettino.notifica_vista !== true;
    
    // Formatta data validazione se presente
    let validazioneInfo = '';
    if (isValidated && bollettino.data_validazione) {
        const dataVal = new Date(bollettino.data_validazione);
        validazioneInfo = `Validato il ${dataVal.toLocaleDateString('it-IT')} alle ${dataVal.toLocaleTimeString('it-IT', { hour: '2-digit', minute: '2-digit' })}`;
        if (bollettino.validato_da) {
            validazioneInfo += ` da ${bollettino.validato_da}`;
        }
    }
    
    document.getElementById('dettaglio-body').innerHTML = `
        <!-- Stato Validazione -->
        <div class="detail-section">
            <div class="detail-section-title">📋 Stato ${isNewNotification ? '<span class="new-badge">NUOVO!</span>' : ''}</div>
            ${isValidated ? `
                <div class="validation-status validated ${isNewNotification ? 'new-validation' : ''}">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                        <polyline points="22 4 12 14.01 9 11.01"/>
                    </svg>
                    <div>
                        <div>Bollettino Validato ${isNewNotification ? '🎉' : ''}</div>
                        ${validazioneInfo ? `<div class="validation-info">${validazioneInfo}</div>` : ''}
                    </div>
                </div>
            ` : `
                <div class="validation-status not-validated">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"/>
                        <line x1="12" y1="8" x2="12" y2="12"/>
                        <line x1="12" y1="16" x2="12.01" y2="16"/>
                    </svg>
                    <div>In attesa di validazione</div>
                </div>
            `}
            
            ${isValidated ? `
                <!-- Stato Fatturazione -->
                ${isInvoiced ? `
                    <div class="invoice-status invoiced">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                            <polyline points="22 4 12 14.01 9 11.01"/>
                        </svg>
                        <div>
                            <div>✅ Completo - Fatturato</div>
                            ${bollettino.data_fatturazione ? `<div class="invoice-info">Fatturato il ${new Date(bollettino.data_fatturazione).toLocaleDateString('it-IT')}${bollettino.fatturato_da ? ` da ${bollettino.fatturato_da}` : ''}</div>` : ''}
                        </div>
                    </div>
                ` : `
                    <div class="invoice-status not-invoiced">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="12" cy="12" r="10"/>
                            <polyline points="12 6 12 12 16 14"/>
                        </svg>
                        <div>In attesa di fatturazione</div>
                    </div>
                `}
            ` : ''}
        </div>
        
        <div class="detail-section">
            <div class="detail-section-title">📅 Informazioni Generali</div>
            <div class="detail-grid">
                <div class="detail-item full-width">
                    <div class="detail-label">Data</div>
                    <div class="detail-value">${dataFormatted}</div>
                </div>
                <div class="detail-item full-width">
                    <div class="detail-label">Cliente</div>
                    <div class="detail-value">${bollettino.cliente || '-'}</div>
                </div>
                <div class="detail-item full-width">
                    <div class="detail-label">Macchina</div>
                    <div class="detail-value">${bollettino.montaggio_macchina || '-'}</div>
                </div>
                <div class="detail-item full-width">
                    <div class="detail-label">Matricola</div>
                    <div class="detail-value">${bollettino.matricola || '-'}</div>
                </div>
                <div class="detail-item full-width">
                    <div class="detail-label">Tecnico</div>
                    <div class="detail-value">${bollettino.tecnico_installatore || '-'}</div>
                </div>
                ${bollettino.email_cliente ? `
                <div class="detail-item full-width">
                    <div class="detail-label">Email Cliente</div>
                    <div class="detail-value">${bollettino.email_cliente}</div>
                </div>
                ` : ''}
            </div>
        </div>
        
        <div class="detail-section">
            <div class="detail-section-title">⏱️ Orari</div>
            <div class="detail-grid">
                <div class="detail-item">
                    <div class="detail-label">Inizio</div>
                    <div class="detail-value">${bollettino.orario_inizio || '-'}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Fine</div>
                    <div class="detail-value">${bollettino.orario_fine || '-'}</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Pausa</div>
                    <div class="detail-value">${bollettino.pausa_minuti || 0} min</div>
                </div>
                <div class="detail-item">
                    <div class="detail-label">Ore Totali</div>
                    <div class="detail-value highlight">${bollettino.ore_totali || 0} h</div>
                </div>
            </div>
        </div>
        
        ${bollettino.lavori_eseguiti ? `
        <div class="detail-section">
            <div class="detail-section-title">📝 Lavori Eseguiti</div>
            <div class="note-box">${bollettino.lavori_eseguiti}</div>
        </div>
        ` : ''}
        
        ${bollettino.note ? `
        <div class="detail-section">
            <div class="detail-section-title">📋 Note</div>
            <div class="note-box">${bollettino.note}</div>
        </div>
        ` : ''}
        
        ${bollettino.foto_prima && bollettino.foto_prima.length > 0 ? `
        <div class="detail-section">
            <div class="detail-section-title">📸 Foto PRIMA (${bollettino.foto_prima.length})</div>
            <div class="detail-photos">
                ${bollettino.foto_prima.map((photo, i) => `
                    <div class="detail-photo" onclick="openPhotoModal('${photo.data}')">
                        <img src="${photo.data}" alt="Prima ${i + 1}">
                    </div>
                `).join('')}
            </div>
        </div>
        ` : ''}
        
        ${bollettino.foto_dopo && bollettino.foto_dopo.length > 0 ? `
        <div class="detail-section">
            <div class="detail-section-title">📸 Foto DOPO (${bollettino.foto_dopo.length})</div>
            <div class="detail-photos">
                ${bollettino.foto_dopo.map((photo, i) => `
                    <div class="detail-photo" onclick="openPhotoModal('${photo.data}')">
                        <img src="${photo.data}" alt="Dopo ${i + 1}">
                    </div>
                `).join('')}
            </div>
        </div>
        ` : ''}
        
        <div class="detail-section">
            <div class="detail-section-title">✍️ Firma Cliente</div>
            ${bollettino.nome_firmatario ? `
                <div class="firmatario-name">
                    <span class="firmatario-label">Firmato da:</span>
                    <span class="firmatario-value">${bollettino.nome_firmatario}</span>
                </div>
            ` : ''}
            ${hasFirma ? `
                <div class="firma-preview">
                    <img src="${bollettino.firma_cliente.data}" alt="Firma cliente">
                </div>
            ` : `
                <div class="no-firma">⚠️ Firma non presente</div>
            `}
        </div>
        
        ${hasPhotos || bollettino.totale_speso ? `
        <div class="detail-section">
            <div class="detail-section-title">🧾 Scontrini Pranzo${hasPhotos ? ` (${bollettino.foto_scontrini.length})` : ''}</div>
            ${hasPhotos ? `
            <div class="detail-photos">
                ${bollettino.foto_scontrini.map((photo, i) => `
                    <div class="detail-photo" onclick="openPhotoModal('${photo.data}')">
                        <img src="${photo.data}" alt="Scontrino ${i + 1}">
                    </div>
                `).join('')}
            </div>
            ` : ''}
            ${bollettino.totale_speso ? `
            <div class="detail-totale-speso">
                <span class="totale-speso-label">💰 Totale Speso</span>
                <span class="totale-speso-value">€ ${parseFloat(bollettino.totale_speso).toFixed(2)}</span>
            </div>
            ` : ''}
        </div>
        ` : ''}
        
        ${isInvoiced ? `
        <!-- Documenti PDF -->
        <div class="detail-section">
            <div class="detail-section-title">📄 Documenti</div>
            <div class="pdf-list">
                ${bollettino.pdf_bollettino_url ? `
                <a href="${bollettino.pdf_bollettino_url}" target="_blank" class="pdf-item">
                    <div class="pdf-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                            <polyline points="14 2 14 8 20 8"/>
                        </svg>
                    </div>
                    <div class="pdf-info">
                        <div class="pdf-name">📋 Bollettino Intervento</div>
                        <div class="pdf-date">PDF Ufficiale</div>
                    </div>
                    <div class="pdf-download">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                            <polyline points="7 10 12 15 17 10"/>
                            <line x1="12" y1="15" x2="12" y2="3"/>
                        </svg>
                    </div>
                </a>
                ` : ''}
                ${hasDDT ? bollettino.ddt_files.map((ddt, i) => `
                <a href="${ddt.url}" target="_blank" class="pdf-item">
                    <div class="pdf-icon" style="background: rgba(16, 185, 129, 0.1);">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="color: #10b981;">
                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                            <polyline points="14 2 14 8 20 8"/>
                        </svg>
                    </div>
                    <div class="pdf-info">
                        <div class="pdf-name">📦 ${ddt.name}</div>
                        <div class="pdf-date">${ddt.uploaded_at ? new Date(ddt.uploaded_at).toLocaleDateString('it-IT') : 'DDT'}</div>
                    </div>
                    <div class="pdf-download">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                            <polyline points="7 10 12 15 17 10"/>
                            <line x1="12" y1="15" x2="12" y2="3"/>
                        </svg>
                    </div>
                </a>
                `).join('') : ''}
            </div>
        </div>
        ` : ''}
    `;
    
    // Gestione permessi pulsanti (usa isMyBollettino già calcolato sopra)
    const isOwner = isMyBollettino;
    
    // Pulsante Modifica: NESSUNO può modificare se validato
    const canEditBollettino = !isValidated && (isOwner || isAdmin);
    document.getElementById('btn-modifica').style.display = canEditBollettino ? 'block' : 'none';
    
    // Azioni Admin/Permessi speciali
    const adminActions = document.getElementById('admin-actions');
    const hasSpecialPermissions = isAdmin || canValidate || canInvoice;
    
    if (hasSpecialPermissions) {
        adminActions.style.display = 'flex';
        
        // Pulsante Valida: visibile solo per chi può validare
        const btnValidate = document.getElementById('btn-validate');
        btnValidate.style.display = canValidate ? 'flex' : 'none';
        const canValidateThis = !isValidated && hasFirma;
        btnValidate.disabled = !canValidateThis;
        
        if (isValidated) {
            btnValidate.innerHTML = `
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                    <polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
                Già Validato
            `;
            btnValidate.title = '';
        } else if (!hasFirma) {
            btnValidate.innerHTML = `
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                    <polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
                Valida
            `;
            btnValidate.title = 'Firma cliente mancante';
        } else {
            btnValidate.innerHTML = `
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                    <polyline points="22 4 12 14.01 9 11.01"/>
                </svg>
                Valida
            `;
            btnValidate.title = '';
        }
        
        // Pulsante Elimina: visibile SOLO per superAdmin (soft delete)
        const btnDelete = document.getElementById('btn-delete');
        btnDelete.style.display = isSuperAdmin ? 'flex' : 'none';
        // SuperAdmin può eliminare anche bollettini validati (soft delete)
        btnDelete.disabled = false;
        btnDelete.title = isSuperAdmin ? 'Elimina bollettino (soft delete)' : '';
        
        // Pulsante Fattura: visibile solo se validato E non fatturato E ha permesso
        const btnInvoice = document.getElementById('btn-invoice');
        const showInvoiceBtn = isValidated && !isInvoiced && canInvoice;
        btnInvoice.style.display = showInvoiceBtn ? 'flex' : 'none';
        
    } else {
        adminActions.style.display = 'none';
    }
    
    document.getElementById('modal-dettaglio').classList.add('show');
    document.body.style.overflow = 'hidden';
    
    // Marca come visto se è una notifica nuova
    if (isNewNotification) {
        markAsViewed(id);
    }
}

function closeModalDettaglio() {
    document.getElementById('modal-dettaglio').classList.remove('show');
    document.body.style.overflow = '';
    currentBollettinoId = null;
}

function openModalModifica() {
    const bollettino = allBollettini.find(b => b.id_bollettino === currentBollettinoId);
    if (!bollettino) return;
    
    // Controlla permessi modifica con confronto flessibile
    const tecnicoCorrente = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim().toLowerCase();
    const tecnicoCorrenteAlt = `${currentUser.cognome || ''} ${currentUser.nome || ''}`.trim().toLowerCase();
    const cognome = (currentUser.cognome || '').toLowerCase();
    const tecnicoDB = (bollettino.tecnico_installatore || '').toLowerCase();
    const isOwner = tecnicoDB === tecnicoCorrente || 
                    tecnicoDB === tecnicoCorrenteAlt ||
                    (cognome && tecnicoDB.includes(cognome));
    const isValidated = bollettino.validato === true;
    
    // Se validato, NESSUNO può modificare
    if (isValidated) {
        alert('Non puoi modificare un bollettino già validato.');
        return;
    }
    
    // Se non è proprietario e non è admin, non può modificare
    if (!isOwner && !isAdmin) {
        alert('Non hai i permessi per modificare questo bollettino.');
        return;
    }
    
    closeModalDettaglio();
    
    // Popola form
    document.getElementById('modal-title').textContent = 'Modifica Bollettino';
    document.getElementById('bollettino-id').value = bollettino.id_bollettino;
    document.getElementById('bollettino-data').value = bollettino.data || '';
    document.getElementById('bollettino-orario-inizio').value = bollettino.orario_inizio || '';
    document.getElementById('bollettino-orario-fine').value = bollettino.orario_fine || '';
    document.getElementById('bollettino-pausa').value = bollettino.pausa_minuti || '';
    document.getElementById('bollettino-ore').textContent = `${bollettino.ore_totali || 0} h`;
    document.getElementById('bollettino-cliente').value = bollettino.cliente || '';
    document.getElementById('bollettino-macchina').value = bollettino.montaggio_macchina || '';
    document.getElementById('bollettino-matricola').value = bollettino.matricola || '';
    document.getElementById('bollettino-tecnico').value = bollettino.tecnico_installatore || '';
    document.getElementById('bollettino-lavori').value = bollettino.lavori_eseguiti || '';
    document.getElementById('bollettino-note').value = bollettino.note || '';
    document.getElementById('bollettino-totale-speso').value = bollettino.totale_speso || '';
    document.getElementById('bollettino-email').value = bollettino.email_cliente || '';
    document.getElementById('bollettino-firmatario').value = bollettino.nome_firmatario || '';
    
    // Carica foto esistenti
    uploadedPhotos = bollettino.foto_scontrini || [];
    renderPhotosGrid();
    
    // Carica foto prima/dopo esistenti
    uploadedFotoPrima = bollettino.foto_prima || [];
    uploadedFotoDopo = bollettino.foto_dopo || [];
    renderFotoPrimaGrid();
    renderFotoDopoGrid();
    
    openModal(false);
    
    // Carica firma esistente
    setTimeout(() => {
        if (bollettino.firma_cliente) {
            loadFirmaToCanvas(bollettino.firma_cliente);
        }
    }, 100);
}

// =============================================
// PHOTO MODAL
// =============================================
function openPhotoModal(src) {
    document.getElementById('photo-preview').src = src;
    document.getElementById('modal-photo').classList.add('show');
    document.body.style.overflow = 'hidden';
}

function closePhotoModal() {
    document.getElementById('modal-photo').classList.remove('show');
    if (!document.getElementById('modal-dettaglio').classList.contains('show')) {
        document.body.style.overflow = '';
    }
}

// =============================================
// CONFERMA DIALOGS
// =============================================
function confirmDelete() {
    const bollettino = allBollettini.find(b => b.id_bollettino === currentBollettinoId);
    if (!bollettino) return;
    
    // Controlla se validato
    if (bollettino.validato === true) {
        alert('Non puoi eliminare un bollettino già validato.');
        return;
    }
    
    document.getElementById('confirm-delete-dialog').classList.add('show');
}

function confirmValidate() {
    const bollettino = allBollettini.find(b => b.id_bollettino === currentBollettinoId);
    if (!bollettino) return;
    
    // Controlla se già validato
    if (bollettino.validato === true) {
        alert('Questo bollettino è già stato validato.');
        return;
    }
    
    // Controlla firma
    const hasFirma = bollettino.firma_cliente && bollettino.firma_cliente.data;
    if (!hasFirma) {
        alert('Non puoi validare un bollettino senza la firma del cliente.');
        return;
    }
    
    // Mostra dialog conferma
    document.getElementById('confirm-validate-dialog').classList.add('show');
}

// Valida bollettino (senza invio email)
async function validateBollettino() {
    if (!currentBollettinoId) return;
    
    const bollettino = allBollettini.find(b => b.id_bollettino === currentBollettinoId);
    
    // Doppio controllo
    if (bollettino && bollettino.validato === true) {
        alert('Questo bollettino è già stato validato.');
        closeConfirmDialog('validate');
        return;
    }
    
    try {
        closeConfirmDialog('validate');
        Loader.show('invio', 'Validazione...');
        
        const validatoDa = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim();
        
        const { error } = await supabaseClient
            .from('BollettiniMontatori')
            .update({
                validato: true,
                data_validazione: new Date().toISOString(),
                validato_da: validatoDa
            })
            .eq('id_bollettino', currentBollettinoId);
        
        if (error) throw error;
        
        // Aggiorna lista
        Loader.updateText('Aggiornamento...');
        await loadBollettini(false);
        
        // Aspetta rendering
        await new Promise(resolve => {
            requestAnimationFrame(() => {
                requestAnimationFrame(resolve);
            });
        });
        
        Loader.hide();
        
        // Mostra messaggio
        alert('✅ Bollettino validato! Non potrà più essere modificato.');
        
        // Riapri dettaglio aggiornato
        openDettaglio(currentBollettinoId);
        
    } catch (error) {
        console.error('Errore validazione:', error);
        alert('Errore durante la validazione: ' + error.message);
        Loader.hide();
    }
}

function closeConfirmDialog(type) {
    if (type === 'delete') {
        document.getElementById('confirm-delete-dialog').classList.remove('show');
    } else if (type === 'validate') {
        document.getElementById('confirm-validate-dialog').classList.remove('show');
    }
}

// =============================================
// GESTIONE EMAIL
// =============================================

// Chiude dialog invio email cliente
function closeEmailDialog(sendEmail) {
    document.getElementById('confirm-email-cliente-dialog').classList.remove('show');
    if (emailResolve) {
        emailResolve(sendEmail);
        emailResolve = null;
    }
}

// Mostra dialog per chiedere se inviare email al cliente
function askSendEmailToCliente(email, bollettinoData) {
    return new Promise((resolve) => {
        emailResolve = resolve;
        pendingEmailData = bollettinoData;
        document.getElementById('email-destinatario').textContent = email;
        document.getElementById('confirm-email-cliente-dialog').classList.add('show');
    });
}

// Carica firma su Supabase Storage e restituisce URL pubblico
async function uploadFirmaToStorage(firmaBase64, bollettinoId) {
    try {
        // Converti base64 in blob
        const base64Data = firmaBase64.replace(/^data:image\/\w+;base64,/, '');
        const byteCharacters = atob(base64Data);
        const byteNumbers = new Array(byteCharacters.length);
        for (let i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        const byteArray = new Uint8Array(byteNumbers);
        const blob = new Blob([byteArray], { type: 'image/png' });
        
        // Nome file unico
        const fileName = `firme/firma_${bollettinoId}_${Date.now()}.png`;
        
        // Upload su Supabase Storage
        const { data, error } = await supabaseClient.storage
            .from('bollettini')
            .upload(fileName, blob, {
                contentType: 'image/png',
                upsert: true
            });
        
        if (error) {
            console.error('Errore upload firma:', error);
            return null;
        }
        
        // Ottieni URL pubblico
        const { data: urlData } = supabaseClient.storage
            .from('bollettini')
            .getPublicUrl(fileName);
        
        console.log('Firma caricata:', urlData.publicUrl);
        return urlData.publicUrl;
        
    } catch (error) {
        console.error('Errore upload firma:', error);
        return null;
    }
}

// Genera PDF del bollettino
function generateBollettinoPDF(bollettinoData) {
    const { jsPDF } = window.jspdf;
    const doc = new jsPDF();
    
    const dataFormatted = new Date(bollettinoData.data).toLocaleDateString('it-IT', {
        weekday: 'long', day: '2-digit', month: 'long', year: 'numeric'
    });
    
    // Colori
    const primaryColor = [220, 38, 38]; // Rosso
    const textColor = [24, 24, 27];
    const mutedColor = [113, 113, 122];
    const bgColor = [250, 250, 250];
    
    const pageHeight = 297;
    const footerHeight = 25;
    const maxY = pageHeight - footerHeight; // 272
    
    // Funzione per controllare se serve nuova pagina
    function checkPageBreak(neededHeight) {
        if (y + neededHeight > maxY) {
            addFooter();
            doc.addPage();
            y = 20;
            return true;
        }
        return false;
    }
    
    // Funzione footer
    function addFooter() {
        doc.setFillColor(...bgColor);
        doc.rect(0, 277, 210, 20, 'F');
        doc.setDrawColor(220, 220, 220);
        doc.line(0, 277, 210, 277);
        doc.setTextColor(...mutedColor);
        doc.setFontSize(8);
        doc.setFont('helvetica', 'normal');
        doc.text('Documento generato automaticamente - Pro System S.r.l. - www.prosystemsrl.eu', 105, 285, { align: 'center' });
        doc.text('Data generazione: ' + new Date().toLocaleString('it-IT'), 105, 291, { align: 'center' });
    }
    
    let y = 20;
    
    // Header con sfondo rosso
    doc.setFillColor(...primaryColor);
    doc.rect(0, 0, 210, 40, 'F');
    
    // Logo a sinistra
    if (logoBase64) {
        // Usa logo reale se precaricato
        try {
            doc.addImage(logoBase64, 'PNG', 8, 8, 24, 24);
        } catch (e) {
            // Fallback: cerchio con PS
            doc.setFillColor(255, 255, 255);
            doc.circle(20, 20, 12, 'F');
            doc.setTextColor(...primaryColor);
            doc.setFontSize(14);
            doc.setFont('helvetica', 'bold');
            doc.text('PS', 20, 24, { align: 'center' });
        }
    } else {
        // Cerchio bianco con PS come placeholder
        doc.setFillColor(255, 255, 255);
        doc.circle(20, 20, 12, 'F');
        doc.setTextColor(...primaryColor);
        doc.setFontSize(14);
        doc.setFont('helvetica', 'bold');
        doc.text('PS', 20, 24, { align: 'center' });
    }
    
    doc.setTextColor(255, 255, 255);
    doc.setFontSize(22);
    doc.setFont('helvetica', 'bold');
    doc.text('BOLLETTINO INTERVENTO', 115, 15, { align: 'center' });
    
    // ID bollettino formattato
    doc.setFontSize(14);
    doc.text(formatBollettinoId(bollettinoData.id_bollettino || 0), 115, 25, { align: 'center' });
    
    doc.setFontSize(11);
    doc.setFont('helvetica', 'normal');
    doc.text('Pro System S.r.l.', 115, 35, { align: 'center' });
    
    y = 55;
    
    // Box info principale - espanso per matricola
    doc.setFillColor(...bgColor);
    doc.roundedRect(15, y - 5, 180, 62, 3, 3, 'F');
    
    doc.setTextColor(...textColor);
    doc.setFontSize(11);
    
    // Riga 1: Data e Tecnico
    doc.setFont('helvetica', 'normal');
    doc.setTextColor(...mutedColor);
    doc.text('DATA', 25, y + 5);
    doc.text('TECNICO', 110, y + 5);
    
    doc.setFont('helvetica', 'bold');
    doc.setTextColor(...textColor);
    doc.text(dataFormatted, 25, y + 13);
    doc.text(bollettinoData.tecnico_installatore || '-', 110, y + 13);
    
    // Riga 2: Cliente e Macchina
    doc.setFont('helvetica', 'normal');
    doc.setTextColor(...mutedColor);
    doc.text('CLIENTE', 25, y + 26);
    doc.text('MACCHINA', 110, y + 26);
    
    doc.setFont('helvetica', 'bold');
    doc.setTextColor(...textColor);
    doc.text(bollettinoData.cliente || '-', 25, y + 34);
    doc.text(bollettinoData.montaggio_macchina || '-', 110, y + 34);
    
    // Riga 3: Matricola
    doc.setFont('helvetica', 'normal');
    doc.setTextColor(...mutedColor);
    doc.text('MATRICOLA', 25, y + 47);
    
    doc.setFont('helvetica', 'bold');
    doc.setTextColor(...textColor);
    doc.text(bollettinoData.matricola || '-', 25, y + 55);
    
    y += 72;
    
    // Box orari
    doc.setFillColor(...bgColor);
    doc.roundedRect(15, y - 5, 180, 30, 3, 3, 'F');
    
    doc.setFont('helvetica', 'normal');
    doc.setTextColor(...mutedColor);
    doc.setFontSize(10);
    doc.text('ORARIO INIZIO', 25, y + 5);
    doc.text('ORARIO FINE', 75, y + 5);
    doc.text('PAUSA', 125, y + 5);
    doc.text('ORE TOTALI', 160, y + 5);
    
    doc.setFont('helvetica', 'bold');
    doc.setTextColor(...textColor);
    doc.setFontSize(12);
    doc.text(bollettinoData.orario_inizio || '-', 25, y + 16);
    doc.text(bollettinoData.orario_fine || '-', 75, y + 16);
    doc.text((bollettinoData.pausa_minuti || 0) + ' min', 125, y + 16);
    
    // Ore totali in rosso
    doc.setTextColor(...primaryColor);
    doc.setFontSize(14);
    doc.text((bollettinoData.ore_totali || 0) + ' h', 160, y + 16);
    
    y += 40;
    
    // Lavori eseguiti
    doc.setTextColor(...textColor);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(12);
    doc.text('LAVORI ESEGUITI', 15, y);
    y += 8;
    
    // Box lavori - sfondo grigio chiaro con bordo sinistro rosso
    const lavori = bollettinoData.lavori_eseguiti || '-';
    const lavoriLines = doc.splitTextToSize(lavori, 165);
    const lavoriHeight = Math.max(lavoriLines.length * 6 + 14, 25);
    
    checkPageBreak(lavoriHeight + 20);
    
    // Sfondo grigio
    doc.setFillColor(...bgColor);
    doc.roundedRect(15, y - 2, 180, lavoriHeight, 2, 2, 'F');
    
    // Bordo sinistro rosso (sovrapposto)
    doc.setFillColor(...primaryColor);
    doc.roundedRect(15, y - 2, 4, lavoriHeight, 2, 0, 'F');
    
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(...textColor);
    doc.text(lavoriLines, 25, y + 8);
    
    y += lavoriHeight + 15;
    
    // Note (sempre visibili)
    checkPageBreak(50);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(12);
    doc.setTextColor(...textColor);
    doc.text('NOTE', 15, y);
    y += 8;
    
    const noteText = (bollettinoData.note && bollettinoData.note !== '-') ? bollettinoData.note : 'Nessuna nota';
    const noteLines = doc.splitTextToSize(noteText, 165);
    const noteHeight = Math.max(noteLines.length * 6 + 14, 25);
    
    checkPageBreak(noteHeight + 20);
    
    doc.setFillColor(...bgColor);
    doc.roundedRect(15, y - 2, 180, noteHeight, 2, 2, 'F');
    
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.setTextColor(...(bollettinoData.note && bollettinoData.note !== '-' ? textColor : mutedColor));
    doc.text(noteLines, 20, y + 8);
    
    y += noteHeight + 15;
    
    // Foto PRIMA intervento
    if (bollettinoData.foto_prima && bollettinoData.foto_prima.length > 0) {
        checkPageBreak(60);
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(12);
        doc.setTextColor(...textColor);
        doc.text('FOTO PRIMA INTERVENTO (' + bollettinoData.foto_prima.length + ')', 15, y);
        y += 8;
        
        let xPos = 15;
        const photoWidth = 40;
        const photoHeight = 40;
        const maxPerRow = 4;
        
        for (let i = 0; i < bollettinoData.foto_prima.length; i++) {
            if (i > 0 && i % maxPerRow === 0) {
                y += photoHeight + 5;
                xPos = 15;
                checkPageBreak(photoHeight + 10);
            }
            
            try {
                doc.addImage(bollettinoData.foto_prima[i].data, 'JPEG', xPos, y, photoWidth, photoHeight);
            } catch (e) {
                doc.setFillColor(...bgColor);
                doc.rect(xPos, y, photoWidth, photoHeight, 'F');
                doc.setFontSize(8);
                doc.setTextColor(...mutedColor);
                doc.text('Foto ' + (i + 1), xPos + photoWidth/2, y + photoHeight/2, { align: 'center' });
            }
            xPos += photoWidth + 5;
        }
        y += photoHeight + 15;
    }
    
    // Foto DOPO intervento
    if (bollettinoData.foto_dopo && bollettinoData.foto_dopo.length > 0) {
        checkPageBreak(60);
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(12);
        doc.setTextColor(...textColor);
        doc.text('FOTO DOPO INTERVENTO (' + bollettinoData.foto_dopo.length + ')', 15, y);
        y += 8;
        
        let xPos = 15;
        const photoWidth = 40;
        const photoHeight = 40;
        const maxPerRow = 4;
        
        for (let i = 0; i < bollettinoData.foto_dopo.length; i++) {
            if (i > 0 && i % maxPerRow === 0) {
                y += photoHeight + 5;
                xPos = 15;
                checkPageBreak(photoHeight + 10);
            }
            
            try {
                doc.addImage(bollettinoData.foto_dopo[i].data, 'JPEG', xPos, y, photoWidth, photoHeight);
            } catch (e) {
                doc.setFillColor(...bgColor);
                doc.rect(xPos, y, photoWidth, photoHeight, 'F');
                doc.setFontSize(8);
                doc.setTextColor(...mutedColor);
                doc.text('Foto ' + (i + 1), xPos + photoWidth/2, y + photoHeight/2, { align: 'center' });
            }
            xPos += photoWidth + 5;
        }
        y += photoHeight + 15;
    }
    
    // Sezione firma
    checkPageBreak(50);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(12);
    doc.setTextColor(...textColor);
    doc.text('FIRMA CLIENTE', 15, y);
    y += 5;
    
    // Nome firmatario
    if (bollettinoData.nome_firmatario) {
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(10);
        doc.setTextColor(...mutedColor);
        doc.text('Firmato da: ' + bollettinoData.nome_firmatario, 15, y + 8);
        y += 12;
    }
    
    // Box firma - dimensione compatta ma leggibile
    const firmaWidth = 50;
    const firmaHeight = 25;
    
    doc.setDrawColor(200, 200, 200);
    doc.setFillColor(255, 255, 255);
    doc.setLineWidth(0.5);
    doc.roundedRect(15, y, firmaWidth, firmaHeight, 3, 3, 'FD');
    
    // Aggiungi immagine firma se presente
    if (bollettinoData.firma_cliente && bollettinoData.firma_cliente.data) {
        try {
            doc.addImage(bollettinoData.firma_cliente.data, 'PNG', 16, y + 1, firmaWidth - 2, firmaHeight - 2);
        } catch (e) {
            console.error('Errore aggiunta firma al PDF:', e);
        }
    }
    
    // Footer finale
    addFooter();
    
    return doc;
}

// Carica PDF su Supabase Storage
async function uploadPDFToStorage(pdfDoc, bollettinoData) {
    try {
        // Genera blob dal PDF
        const pdfBlob = pdfDoc.output('blob');
        
        // Nome file con formato #0001
        const idFormatted = formatBollettinoId(bollettinoData.id_bollettino || 0).replace('#', '');
        const dataFile = bollettinoData.data ? bollettinoData.data.replace(/-/g, '') : 'data';
        const clienteFile = (bollettinoData.cliente || 'cliente').replace(/[^a-zA-Z0-9]/g, '_').substring(0, 20);
        const fileName = `pdf/Bollettino_${idFormatted}_${clienteFile}_${dataFile}.pdf`;
        
        // Upload su Supabase Storage
        const { data, error } = await supabaseClient.storage
            .from('bollettini')
            .upload(fileName, pdfBlob, {
                contentType: 'application/pdf',
                upsert: true
            });
        
        if (error) {
            console.error('Errore upload PDF:', error);
            return null;
        }
        
        // Ottieni URL pubblico
        const { data: urlData } = supabaseClient.storage
            .from('bollettini')
            .getPublicUrl(fileName);
        
        console.log('PDF caricato:', urlData.publicUrl);
        return urlData.publicUrl;
        
    } catch (error) {
        console.error('Errore upload PDF:', error);
        return null;
    }
}

// Invia email al cliente
async function sendEmailToCliente(bollettinoData) {
    if (EMAILJS_CONFIG.publicKey === 'YOUR_PUBLIC_KEY') {
        console.warn('EmailJS non configurato');
        return false;
    }
    
    try {
        const dataFormatted = new Date(bollettinoData.data).toLocaleDateString('it-IT', {
            day: '2-digit', month: 'long', year: 'numeric'
        });
        
        // Carica firma su Storage e ottieni URL pubblico
        let firmaUrl = '';
        if (bollettinoData.firma_cliente && bollettinoData.firma_cliente.data) {
            firmaUrl = await uploadFirmaToStorage(
                bollettinoData.firma_cliente.data, 
                bollettinoData.id_bollettino || Date.now()
            );
        }
        
        // Genera PDF e caricalo su Storage
        let pdfUrl = '';
        try {
            const pdfDoc = generateBollettinoPDF(bollettinoData);
            pdfUrl = await uploadPDFToStorage(pdfDoc, bollettinoData);
        } catch (e) {
            console.error('Errore generazione PDF:', e);
        }
        
        // Prepara HTML firma con nome firmatario
        let firmaHtml = '';
        if (bollettinoData.nome_firmatario) {
            firmaHtml += `<p><strong>Firmato da:</strong> ${bollettinoData.nome_firmatario}</p>`;
        }
        if (firmaUrl) {
            firmaHtml += `<img src="${firmaUrl}" alt="Firma Cliente" style="max-width:300px; border:1px solid #ccc; border-radius:8px; padding:10px; background:#fff;">`;
        } else {
            firmaHtml += '<em>Firma non disponibile</em>';
        }
        
        // Prepara HTML link PDF
        let pdfHtml = '';
        if (pdfUrl) {
            pdfHtml = `<a href="${pdfUrl}" style="display:inline-block; background:#dc2626; color:#ffffff; padding:12px 24px; border-radius:8px; text-decoration:none; font-weight:bold;">📄 Scarica PDF Bollettino</a>`;
        }
        
        console.log('Firma URL:', firmaUrl);
        console.log('PDF URL:', pdfUrl);
        
        const response = await emailjs.send(
            EMAILJS_CONFIG.serviceId,
            EMAILJS_CONFIG.templateClienteId,
            {
                to_email: bollettinoData.email_cliente,
                to_name: 'cliente',  // Fisso "Gentile cliente"
                data_intervento: dataFormatted,
                tecnico: bollettinoData.tecnico_installatore,
                macchina: bollettinoData.montaggio_macchina || 'Non specificata',
                matricola: bollettinoData.matricola || 'Non specificata',
                ore_lavorate: bollettinoData.ore_totali + ' ore',
                orario: `${bollettinoData.orario_inizio || ''} - ${bollettinoData.orario_fine || ''}`,
                lavori_eseguiti: bollettinoData.lavori_eseguiti || '-',
                note: bollettinoData.note || '-',
                firma_html: firmaHtml,
                pdf_link: pdfHtml
            }
        );
        
        console.log('Email inviata:', response);
        return true;
    } catch (error) {
        console.error('Errore invio email:', error);
        return false;
    }
}

// Carica lista admin per selezione
async function loadAdminList() {
    try {
        const { data, error } = await supabaseClient
            .from('accounts')
            .select('id, nome, cognome, email, ruolo')
            .or('ruolo.ilike.%admin%,ruolo.ilike.%amministratore%,ruolo.ilike.%manager%,is_admin.eq.true');
        
        if (error) throw error;
        
        adminList = (data || []).filter(u => u.email); // Solo quelli con email
        return adminList;
    } catch (error) {
        console.error('Errore caricamento admin:', error);
        return [];
    }
}

// Mostra dialog selezione admin
async function showAdminSelectDialog() {
    return new Promise(async (resolve) => {
        emailResolve = resolve;
        selectedAdminEmail = null;
        
        // Carica lista admin
        const admins = await loadAdminList();
        
        const container = document.getElementById('admin-select-list');
        
        if (admins.length === 0) {
            container.innerHTML = `
                <p style="text-align: center; color: var(--text-muted); padding: 20px;">
                    Nessun admin con email trovato
                </p>
            `;
        } else {
            container.innerHTML = admins.map((admin, i) => `
                <label class="admin-select-item ${i === 0 ? 'selected' : ''}" onclick="selectAdmin('${admin.email}', this)">
                    <input type="radio" name="admin-select" value="${admin.email}" ${i === 0 ? 'checked' : ''}>
                    <span class="admin-select-radio"></span>
                    <div class="admin-select-info">
                        <div class="admin-select-name">${admin.nome || ''} ${admin.cognome || ''}</div>
                        <div class="admin-select-email">${admin.email}</div>
                    </div>
                </label>
            `).join('');
            
            // Seleziona primo admin di default
            if (admins.length > 0) {
                selectedAdminEmail = admins[0].email;
            }
        }
        
        document.getElementById('select-admin-dialog').classList.add('show');
    });
}

// Seleziona admin dalla lista
function selectAdmin(email, element) {
    selectedAdminEmail = email;
    
    // Rimuovi selezione da tutti
    document.querySelectorAll('.admin-select-item').forEach(item => {
        item.classList.remove('selected');
    });
    
    // Aggiungi selezione a quello cliccato
    element.classList.add('selected');
    element.querySelector('input').checked = true;
}

// Chiude dialog selezione admin
function closeAdminSelectDialog(proceed) {
    document.getElementById('select-admin-dialog').classList.remove('show');
    if (emailResolve) {
        emailResolve(proceed ? selectedAdminEmail : null);
        emailResolve = null;
    }
}

// Invia notifica validazione ad admin
async function sendValidationNotification(adminEmail, bollettinoData) {
    if (EMAILJS_CONFIG.publicKey === 'YOUR_PUBLIC_KEY') {
        console.warn('EmailJS non configurato');
        return false;
    }
    
    try {
        const dataFormatted = new Date(bollettinoData.data).toLocaleDateString('it-IT', {
            day: '2-digit', month: 'long', year: 'numeric'
        });
        
        const validatoDa = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim();
        
        const response = await emailjs.send(
            EMAILJS_CONFIG.serviceId,
            EMAILJS_CONFIG.templateAdminId,
            {
                to_email: adminEmail,
                validato_da: validatoDa,
                cliente: bollettinoData.cliente,
                data_intervento: dataFormatted,
                tecnico: bollettinoData.tecnico_installatore,
                macchina: bollettinoData.montaggio_macchina || 'Non specificata',
                matricola: bollettinoData.matricola || 'Non specificata',
                ore_lavorate: bollettinoData.ore_totali + ' ore',
                lavori_eseguiti: bollettinoData.lavori_eseguiti || '-'
            }
        );
        
        console.log('Notifica admin inviata:', response);
        return true;
    } catch (error) {
        console.error('Errore invio notifica:', error);
        return false;
    }
}

// =============================================
// FATTURAZIONE
// =============================================

// Apri modal fatturazione
function openInvoiceModal(bollettinoId) {
    const bollettino = allBollettini.find(b => b.id_bollettino === bollettinoId);
    if (!bollettino) return;
    
    currentBollettinoId = bollettinoId;
    uploadedDDTs = [];
    
    // Popola info bollettino compatto
    const dataFormatted = new Date(bollettino.data).toLocaleDateString('it-IT', {
        weekday: 'short', day: '2-digit', month: 'short', year: 'numeric'
    });
    
    document.getElementById('invoice-cliente').textContent = bollettino.cliente || '-';
    document.getElementById('invoice-macchina').textContent = bollettino.montaggio_macchina + (bollettino.matricola ? ` (${bollettino.matricola})` : '') || '-';
    document.getElementById('invoice-data').textContent = dataFormatted;
    
    // Reset form
    document.getElementById('uploaded-ddt-list').innerHTML = '';
    document.getElementById('invoice-notes').value = '';
    document.getElementById('btn-complete-invoice').disabled = true;
    
    // Mostra modal
    document.getElementById('modal-fatturazione').classList.add('show');
    document.body.style.overflow = 'hidden';
}

// Chiudi modal fatturazione
function closeInvoiceModal() {
    document.getElementById('modal-fatturazione').classList.remove('show');
    document.body.style.overflow = '';
    uploadedDDTs = [];
}

// Gestisci upload DDT
async function handleDDTUpload(event) {
    const files = event.target.files;
    if (!files || files.length === 0) return;
    
    Loader.show('invio', 'Caricamento DDT...');
    
    for (const file of files) {
        if (file.type !== 'application/pdf') {
            alert('Solo file PDF sono accettati');
            continue;
        }
        
        try {
            // Converti in base64
            const base64 = await new Promise((resolve, reject) => {
                const reader = new FileReader();
                reader.onload = () => resolve(reader.result);
                reader.onerror = reject;
                reader.readAsDataURL(file);
            });
            
            // Carica su Supabase Storage
            const fileName = `ddt/DDT_${currentBollettinoId}_${Date.now()}_${file.name}`;
            const blob = await fetch(base64).then(r => r.blob());
            
            const { data, error } = await supabaseClient.storage
                .from('bollettini')
                .upload(fileName, blob, {
                    contentType: 'application/pdf',
                    upsert: true
                });
            
            if (error) throw error;
            
            // Ottieni URL pubblico
            const { data: urlData } = supabaseClient.storage
                .from('bollettini')
                .getPublicUrl(fileName);
            
            // Aggiungi alla lista
            uploadedDDTs.push({
                name: file.name,
                url: urlData.publicUrl,
                uploaded_at: new Date().toISOString()
            });
            
        } catch (e) {
            console.error('Errore upload DDT:', e);
            alert('Errore caricamento: ' + file.name);
        }
    }
    
    // Aggiorna UI
    renderUploadedDDTs();
    event.target.value = ''; // Reset input
    
    Loader.hide();
}

// Render lista DDT caricati
function renderUploadedDDTs() {
    const container = document.getElementById('uploaded-ddt-list');
    
    if (uploadedDDTs.length === 0) {
        container.innerHTML = '';
        document.getElementById('btn-complete-invoice').disabled = true;
        return;
    }
    
    container.innerHTML = uploadedDDTs.map((ddt, i) => `
        <div class="ddt-file-item">
            <div class="ddt-file-icon">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                    <polyline points="14 2 14 8 20 8"/>
                </svg>
            </div>
            <span class="ddt-file-name">${ddt.name}</span>
            <button class="ddt-file-remove" onclick="removeDDT(${i})">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="18" y1="6" x2="6" y2="18"/>
                    <line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
            </button>
        </div>
    `).join('');
    
    document.getElementById('btn-complete-invoice').disabled = false;
}

// Rimuovi DDT dalla lista
function removeDDT(index) {
    uploadedDDTs.splice(index, 1);
    renderUploadedDDTs();
}

// Completa fatturazione
async function completeInvoice() {
    console.log('=== COMPLETE INVOICE START ===');
    console.log('DDT caricati:', uploadedDDTs.length, uploadedDDTs);
    
    if (uploadedDDTs.length === 0) {
        alert('Carica almeno un DDT');
        return;
    }
    
    const bollettino = allBollettini.find(b => b.id_bollettino === currentBollettinoId);
    console.log('Bollettino trovato:', bollettino);
    console.log('Email cliente:', bollettino?.email_cliente);
    
    if (!bollettino) return;
    
    try {
        Loader.show('invio', 'Completamento fatturazione...');
        
        const fatturatoDa = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim();
        
        // Prima genera e salva il PDF finale del bollettino
        let pdfBollettinoUrl = bollettino.pdf_bollettino_url;
        if (!pdfBollettinoUrl) {
            try {
                const pdfDoc = generateBollettinoPDF(bollettino);
                pdfBollettinoUrl = await uploadPDFToStorage(pdfDoc, bollettino);
                console.log('PDF generato:', pdfBollettinoUrl);
            } catch (e) {
                console.error('Errore generazione PDF bollettino:', e);
            }
        }
        
        // Aggiorna database
        console.log('Aggiornamento database...');
        const { error } = await supabaseClient
            .from('BollettiniMontatori')
            .update({
                fatturato: true,
                data_fatturazione: new Date().toISOString(),
                fatturato_da: fatturatoDa,
                ddt_files: uploadedDDTs,
                pdf_bollettino_url: pdfBollettinoUrl,
                note_fatturazione: document.getElementById('invoice-notes').value || null
            })
            .eq('id_bollettino', currentBollettinoId);
        
        if (error) {
            console.error('Errore database:', error);
            throw error;
        }
        console.log('Database aggiornato OK');
        
        // Invia email al cliente se ha email
        if (bollettino.email_cliente) {
            console.log('Invio email a:', bollettino.email_cliente);
            Loader.updateText('Invio email cliente...');
            const emailResult = await sendInvoiceEmail(bollettino, uploadedDDTs, pdfBollettinoUrl);
            console.log('Risultato email:', emailResult);
        } else {
            console.log('Nessuna email cliente, skip invio');
        }
        
        closeInvoiceModal();
        closeModalDettaglio();
        
        Loader.updateText('Aggiornamento lista...');
        await loadBollettini(false);
        
        Loader.hide();
        console.log('=== COMPLETE INVOICE DONE ===');
        
    } catch (e) {
        console.error('Errore fatturazione:', e);
        Loader.hide();
        alert('Errore durante la fatturazione: ' + e.message);
    }
}

// Invia email fatturazione al cliente
async function sendInvoiceEmail(bollettinoData, ddtFiles, pdfBollettinoUrl) {
    if (!EMAILJS_CONFIG.publicKey || EMAILJS_CONFIG.publicKey === 'YOUR_PUBLIC_KEY') {
        console.warn('EmailJS non configurato');
        return false;
    }
    
    try {
        const dataFormatted = new Date(bollettinoData.data).toLocaleDateString('it-IT', {
            day: '2-digit', month: 'long', year: 'numeric'
        });
        
        // Prepara HTML per DDT
        let ddtHtml = '';
        ddtFiles.forEach((ddt, i) => {
            ddtHtml += `<a href="${ddt.url}" style="display:inline-block; background:#10b981; color:#ffffff; padding:10px 20px; border-radius:8px; text-decoration:none; font-weight:bold; margin:4px;">📄 ${ddt.name}</a><br>`;
        });
        
        // Link al PDF bollettino
        let bollettinoHtml = '';
        if (pdfBollettinoUrl) {
            bollettinoHtml = `<a href="${pdfBollettinoUrl}" style="display:inline-block; background:#dc2626; color:#ffffff; padding:10px 20px; border-radius:8px; text-decoration:none; font-weight:bold; margin:4px;">📋 Bollettino Intervento</a>`;
        }
        
        const response = await emailjs.send(
            EMAILJS_CONFIG.serviceId,
            EMAILJS_CONFIG.templateFatturazioneId, // Template specifico per fatturazione
            {
                to_email: bollettinoData.email_cliente,
                to_name: 'cliente',
                subject: 'Documentazione Intervento - Pro System S.r.l.',
                data_intervento: dataFormatted,
                tecnico: bollettinoData.tecnico_installatore,
                macchina: bollettinoData.montaggio_macchina || 'Non specificata',
                matricola: bollettinoData.matricola || 'Non specificata',
                ore_lavorate: bollettinoData.ore_totali + ' ore',
                orario: `${bollettinoData.orario_inizio || ''} - ${bollettinoData.orario_fine || ''}`,
                lavori_eseguiti: bollettinoData.lavori_eseguiti || '-',
                note: bollettinoData.note || '-',
                firma_html: bollettinoData.nome_firmatario ? `<p>Firmato da: ${bollettinoData.nome_firmatario}</p>` : '',
                pdf_link: `${ddtHtml}<br>${bollettinoHtml}`,
                is_invoice: true
            }
        );
        
        console.log('Email fatturazione inviata:', response);
        return true;
    } catch (error) {
        console.error('Errore invio email fatturazione:', error);
        return false;
    }
}

// =============================================
// ELIMINA BOLLETTINO (SOFT DELETE)
// =============================================
async function deleteBollettino() {
    if (!currentBollettinoId) return;
    
    const bollettino = allBollettini.find(b => b.id_bollettino === currentBollettinoId);
    
    // Solo superAdmin può eliminare bollettini validati
    if (bollettino && bollettino.validato === true && !isSuperAdmin) {
        alert('Non puoi eliminare un bollettino già validato.');
        closeConfirmDialog('delete');
        return;
    }
    
    // Check permessi: solo superAdmin può eliminare
    if (!isSuperAdmin) {
        alert('Solo i Super Admin possono eliminare i bollettini.');
        closeConfirmDialog('delete');
        return;
    }
    
    try {
        closeConfirmDialog('delete');
        Loader.show('invio', 'Eliminazione...');
        
        const eliminatoDa = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim();
        
        // SOFT DELETE: marca come eliminato invece di cancellare
        const { error } = await supabaseClient
            .from('BollettiniMontatori')
            .update({
                eliminato: true,
                data_eliminazione: new Date().toISOString(),
                eliminato_da: eliminatoDa
            })
            .eq('id_bollettino', currentBollettinoId);
        
        if (error) throw error;
        
        closeModalDettaglio();
        
        // Aggiorna testo
        Loader.updateText('Aggiornamento lista...');
        
        // Ricarica senza loader interno
        await loadBollettini(false);
        
        // Aspetta rendering
        await new Promise(resolve => {
            requestAnimationFrame(() => {
                requestAnimationFrame(resolve);
            });
        });
        
    } catch (error) {
        console.error('Errore eliminazione:', error);
        alert('Errore durante l\'eliminazione: ' + error.message);
    } finally {
        Loader.hide();
    }
}

// =============================================
// VALIDA BOLLETTINO
// =============================================
async function validateBollettino() {
    if (!currentBollettinoId) return;
    
    const bollettino = allBollettini.find(b => b.id_bollettino === currentBollettinoId);
    
    // Doppio controllo validazione
    if (bollettino && bollettino.validato === true) {
        alert('Questo bollettino è già stato validato.');
        closeConfirmDialog('validate');
        return;
    }
    
    // Controllo firma
    const hasFirma = bollettino && bollettino.firma_cliente && bollettino.firma_cliente.data;
    if (!hasFirma) {
        alert('Non puoi validare un bollettino senza la firma del cliente.');
        closeConfirmDialog('validate');
        return;
    }
    
    try {
        closeConfirmDialog('validate');
        Loader.show('invio', 'Validazione...');
        
        const validatoDa = `${currentUser.nome || ''} ${currentUser.cognome || ''}`.trim();
        
        const { error } = await supabaseClient
            .from('BollettiniMontatori')
            .update({
                validato: true,
                data_validazione: new Date().toISOString(),
                validato_da: validatoDa
            })
            .eq('id_bollettino', currentBollettinoId);
        
        if (error) throw error;
        
        // Aggiorna testo
        Loader.updateText('Aggiornamento...');
        
        // Aggiorna lista senza loader interno
        await loadBollettini(false);
        
        // Aspetta rendering
        await new Promise(resolve => {
            requestAnimationFrame(() => {
                requestAnimationFrame(resolve);
            });
        });
        
        // Nascondi loader prima di riaprire dettaglio
        Loader.hide();
        
        // Riapri dettaglio aggiornato
        openDettaglio(currentBollettinoId);
        
    } catch (error) {
        console.error('Errore validazione:', error);
        alert('Errore durante la validazione: ' + error.message);
        Loader.hide();
    }
}

// =============================================
// LOGOUT
// =============================================
function logout() {
    sessionStorage.removeItem('plm_logged_in');
    sessionStorage.removeItem('plm_user');
    localStorage.removeItem('plm_logged_in');
    localStorage.removeItem('plm_user');
    window.location.href = 'login.html';
}

// =============================================
// INIT
// =============================================
document.addEventListener('DOMContentLoaded', async function() {
    // Mostra loader SUBITO
    if (typeof Loader !== 'undefined') {
        Loader.show('pagina', 'Caricamento bollettini...');
    }
    
    try {
        // Inizializza
        loadUserData();
        await initSupabase();
        await loadUserPermissions(); // Carica permessi dal DB
        initEmailJS(); // Inizializza EmailJS
        preloadLogo(); // Precarica logo per PDF
        
        // Carica bollettini SENZA loader interno (usa quello pagina)
        await loadBollettini(false);
        
        // Calcola padding iniziale
        updateContentPadding();
        
        // Event listeners per calcolo ore
        document.getElementById('bollettino-orario-inizio').addEventListener('change', calcOre);
        document.getElementById('bollettino-orario-fine').addEventListener('change', calcOre);
        document.getElementById('bollettino-pausa').addEventListener('input', calcOre);
        
        // ASPETTA che il browser abbia renderizzato le card
        await new Promise(resolve => {
            requestAnimationFrame(() => {
                requestAnimationFrame(resolve);
            });
        });
        
    } catch (error) {
        console.error('Errore inizializzazione:', error);
    } finally {
        // Nascondi loader SOLO quando tutto è renderizzato
        if (typeof Loader !== 'undefined') {
            Loader.hide();
        }
        
        // Fix scroll DOPO loader hide
        setTimeout(() => {
            document.body.style.overflow = '';
            document.body.style.overflowY = 'auto';
            document.documentElement.style.overflow = '';
            document.documentElement.style.overflowY = 'auto';
        }, 350);
    }
});

// =============================================
// FATTURAZIONE BATCH - FUNZIONI
// =============================================

function toggleBatchMode() {
    batchMode = !batchMode;
    selectedBollettiniIds.clear();
    
    const btn = document.getElementById('btn-batch-mode');
    const content = document.getElementById('page-content');
    
    if (batchMode) {
        btn.classList.add('active');
        btn.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg> Annulla';
        content.classList.add('batch-mode');
        
        // Segna card selezionabili (solo validati non fatturati)
        document.querySelectorAll('.bollettino-card').forEach(card => {
            const id = parseInt(card.dataset.id);
            const bollettino = allBollettini.find(b => b.id_bollettino === id);
            if (bollettino && bollettino.validato && !bollettino.fatturato) {
                card.classList.remove('not-selectable');
            } else {
                card.classList.add('not-selectable');
            }
        });
    } else {
        btn.classList.remove('active');
        btn.innerHTML = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/></svg> Multi';
        content.classList.remove('batch-mode');
        document.querySelectorAll('.bollettino-card').forEach(card => {
            card.classList.remove('not-selectable', 'selected');
        });
        document.querySelectorAll('.card-checkbox').forEach(cb => {
            cb.classList.remove('checked');
        });
    }
    
    updateBatchSelectionBar();
}

function toggleBollettinoSelection(id, event) {
    if (!batchMode) return;
    if (event) event.stopPropagation();
    
    const bollettino = allBollettini.find(b => b.id_bollettino === id);
    if (!bollettino || !bollettino.validato || bollettino.fatturato) return;
    
    const card = document.querySelector('.bollettino-card[data-id="' + id + '"]');
    const checkbox = card.querySelector('.card-checkbox');
    
    if (selectedBollettiniIds.has(id)) {
        selectedBollettiniIds.delete(id);
        card.classList.remove('selected');
        checkbox.classList.remove('checked');
    } else {
        selectedBollettiniIds.add(id);
        card.classList.add('selected');
        checkbox.classList.add('checked');
    }
    
    updateBatchSelectionBar();
}

function updateBatchSelectionBar() {
    const bar = document.getElementById('batch-selection-bar');
    const count = document.getElementById('batch-count');
    const invoiceBtn = document.getElementById('btn-batch-invoice-action');
    
    if (batchMode && selectedBollettiniIds.size > 0) {
        bar.classList.add('show');
        count.textContent = selectedBollettiniIds.size;
        invoiceBtn.disabled = false;
    } else {
        bar.classList.remove('show');
    }
}

function openBatchInvoiceModal() {
    if (selectedBollettiniIds.size === 0) return;
    
    batchUploadedDDTs = [];
    
    const listEl = document.getElementById('batch-bollettini-list');
    const bollettiniSelezionati = allBollettini.filter(b => selectedBollettiniIds.has(b.id_bollettino));
    
    listEl.innerHTML = bollettiniSelezionati.map(b => {
        const dataFormatted = new Date(b.data).toLocaleDateString('it-IT', { day: '2-digit', month: '2-digit' });
        return '<div class="batch-bollettino-item" data-id="' + b.id_bollettino + '">' +
            '<div class="batch-bollettino-info">' +
                '<span class="batch-bollettino-id">' + formatBollettinoId(b.id_bollettino) + '</span>' +
                '<span class="batch-bollettino-cliente">' + (b.cliente || '-') + '</span>' +
                '<span class="batch-bollettino-details">' + dataFormatted + ' - ' + (b.ore_totali || 0) + 'h - ' + (b.matricola || '-') + '</span>' +
            '</div>' +
            '<button class="btn-remove-bollettino" onclick="removeBollettinoFromBatch(' + b.id_bollettino + ')">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>' +
            '</button>' +
        '</div>';
    }).join('');
    
    // Pre-popola email
    const firstWithEmail = bollettiniSelezionati.find(b => b.email_fatturazione || b.email_cliente);
    document.getElementById('batch-email-cliente').value = firstWithEmail ? (firstWithEmail.email_fatturazione || firstWithEmail.email_cliente || '') : '';
    
    // Reset
    document.getElementById('batch-ddt-list').innerHTML = '';
    document.getElementById('batch-invoice-notes').value = '';
    document.getElementById('btn-complete-batch-invoice').disabled = true;
    
    // Mostra modale
    document.getElementById('modal-fatturazione-batch').classList.add('show');
    document.body.style.overflow = 'hidden';
}

function removeBollettinoFromBatch(id) {
    selectedBollettiniIds.delete(id);
    
    const item = document.querySelector('.batch-bollettino-item[data-id="' + id + '"]');
    if (item) item.remove();
    
    const card = document.querySelector('.bollettino-card[data-id="' + id + '"]');
    if (card) {
        card.classList.remove('selected');
        const checkbox = card.querySelector('.card-checkbox');
        if (checkbox) checkbox.classList.remove('checked');
    }
    
    updateBatchSelectionBar();
    
    if (selectedBollettiniIds.size === 0) {
        closeBatchInvoiceModal();
    }
}

function closeBatchInvoiceModal() {
    document.getElementById('modal-fatturazione-batch').classList.remove('show');
    document.body.style.overflow = '';
    batchUploadedDDTs = [];
}

async function handleBatchDDTUpload(event) {
    const files = event.target.files;
    if (!files || files.length === 0) return;
    
    Loader.show('invio', 'Caricamento DDT...');
    
    for (const file of files) {
        if (file.type !== 'application/pdf') {
            alert('Solo file PDF sono accettati');
            continue;
        }
        
        try {
            const fileName = 'ddt/batch_' + Date.now() + '_' + file.name;
            const { data, error } = await supabaseClient.storage
                .from('bollettini')
                .upload(fileName, file, { contentType: 'application/pdf', upsert: true });
            
            if (error) throw error;
            
            const { data: urlData } = supabaseClient.storage
                .from('bollettini')
                .getPublicUrl(fileName);
            
            batchUploadedDDTs.push({
                name: file.name,
                url: urlData.publicUrl,
                path: fileName
            });
        } catch (e) {
            console.error('Errore upload DDT:', e);
        }
    }
    
    renderBatchDDTList();
    event.target.value = '';
    Loader.hide();
}

function renderBatchDDTList() {
    const listEl = document.getElementById('batch-ddt-list');
    
    listEl.innerHTML = batchUploadedDDTs.map((ddt, i) => 
        '<div class="batch-ddt-item">' +
            '<div class="batch-ddt-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg></div>' +
            '<span class="batch-ddt-name">' + ddt.name + '</span>' +
            '<button class="btn-remove-ddt" onclick="removeBatchDDT(' + i + ')"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button>' +
        '</div>'
    ).join('');
    
    document.getElementById('btn-complete-batch-invoice').disabled = batchUploadedDDTs.length === 0;
}

function removeBatchDDT(index) {
    batchUploadedDDTs.splice(index, 1);
    renderBatchDDTList();
}

async function completeBatchInvoice() {
    const emailCliente = document.getElementById('batch-email-cliente').value.trim();
    
    if (!emailCliente) {
        alert('⚠️ Email obbligatoria!\n\nInserisci l\'email del cliente.');
        document.getElementById('batch-email-cliente').focus();
        return;
    }
    
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(emailCliente)) {
        alert('⚠️ Email non valida!');
        document.getElementById('batch-email-cliente').focus();
        return;
    }
    
    if (batchUploadedDDTs.length === 0) {
        alert('Carica almeno un DDT');
        return;
    }
    
    const notes = document.getElementById('batch-invoice-notes').value.trim();
    
    try {
        Loader.show('invio', 'Fatturazione in corso...');
        
        const fatturatoDa = (currentUser.nome || '') + ' ' + (currentUser.cognome || '');
        const idsArray = Array.from(selectedBollettiniIds);
        const bollettiniDaFatturare = allBollettini.filter(b => idsArray.includes(b.id_bollettino));
        
        let pdfBollettiniUrls = [];
        
        // Genera PDF per ogni bollettino
        for (let i = 0; i < bollettiniDaFatturare.length; i++) {
            const bollettino = bollettiniDaFatturare[i];
            Loader.updateText('Generazione PDF ' + (i + 1) + '/' + bollettiniDaFatturare.length + '...');
            
            let pdfUrl = bollettino.pdf_bollettino_url;
            if (!pdfUrl) {
                try {
                    const pdfDoc = generateBollettinoPDF(bollettino);
                    pdfUrl = await uploadPDFToStorage(pdfDoc, bollettino);
                } catch (e) {
                    console.error('Errore generazione PDF:', e);
                }
            }
            
            if (pdfUrl) {
                pdfBollettiniUrls.push({ id: bollettino.id_bollettino, url: pdfUrl, cliente: bollettino.cliente });
            }
        }
        
        // Aggiorna database
        Loader.updateText('Aggiornamento database...');
        for (const id of idsArray) {
            const pdfInfo = pdfBollettiniUrls.find(p => p.id === id);
            await supabaseClient
                .from('BollettiniMontatori')
                .update({
                    fatturato: true,
                    data_fatturazione: new Date().toISOString(),
                    fatturato_da: fatturatoDa.trim(),
                    ddt_files: batchUploadedDDTs,
                    pdf_bollettino_url: pdfInfo ? pdfInfo.url : null,
                    note_fatturazione: notes || null
                })
                .eq('id_bollettino', id);
        }
        
        // Invia email
        Loader.updateText('Invio email...');
        await sendBatchInvoiceEmail(emailCliente, bollettiniDaFatturare, batchUploadedDDTs, pdfBollettiniUrls, notes);
        
        closeBatchInvoiceModal();
        toggleBatchMode();
        
        await loadBollettini(false);
        Loader.hide();
        
    } catch (e) {
        console.error('Errore fatturazione batch:', e);
        Loader.hide();
        alert('Errore: ' + e.message);
    }
}

async function sendBatchInvoiceEmail(email, bollettini, ddtFiles, pdfUrls, notes) {
    if (!EMAILJS_CONFIG.publicKey || EMAILJS_CONFIG.publicKey === 'YOUR_PUBLIC_KEY') {
        console.warn('EmailJS non configurato');
        return false;
    }
    
    try {
        let riepilogo = '';
        let totaleOre = 0;
        
        bollettini.forEach(b => {
            const dataFormatted = new Date(b.data).toLocaleDateString('it-IT', { day: '2-digit', month: '2-digit' });
            riepilogo += '• ' + formatBollettinoId(b.id_bollettino) + ' - ' + dataFormatted + ' - ' + b.cliente + ' (' + (b.ore_totali || 0) + 'h)\n';
            totaleOre += parseFloat(b.ore_totali) || 0;
        });
        
        let bollettiniLinks = pdfUrls.map(pdf => '📋 ' + formatBollettinoId(pdf.id) + ': ' + pdf.url).join('\n');
        let ddtLinks = ddtFiles.map(ddt => '📄 ' + ddt.name + ': ' + ddt.url).join('\n');
        
        const templateParams = {
            to_email: email,
            subject: 'Documentazione Interventi ' + bollettini.map(b => formatBollettinoId(b.id_bollettino)).join(', ') + ' - Pro System S.r.l.',
            riepilogo_interventi: riepilogo,
            totale_ore: totaleOre.toFixed(1),
            num_interventi: bollettini.length,
            bollettini_links: bollettiniLinks,
            ddt_links: ddtLinks,
            note: notes || 'Nessuna nota',
            data_invio: new Date().toLocaleString('it-IT')
        };
        
        await emailjs.send(
            EMAILJS_CONFIG.serviceId,
            EMAILJS_CONFIG.templateFatturazioneId,
            templateParams,
            EMAILJS_CONFIG.publicKey
        );
        
        return true;
    } catch (e) {
        console.error('Errore invio email batch:', e);
        return false;
    }
}

// =============================================
// FOTO PRIMA/DOPO INTERVENTO
// =============================================

function handleFotoPrimaUpload(event) {
    const files = event.target.files;
    if (!files) return;
    
    Array.from(files).forEach(file => {
        if (file.type.startsWith('image/')) {
            const reader = new FileReader();
            reader.onload = e => {
                uploadedFotoPrima.push({ data: e.target.result, name: file.name });
                renderFotoPrimaGrid();
            };
            reader.readAsDataURL(file);
        }
    });
    event.target.value = '';
}

function removeFotoPrima(index) {
    uploadedFotoPrima.splice(index, 1);
    renderFotoPrimaGrid();
}

function renderFotoPrimaGrid() {
    const grid = document.getElementById('foto-prima-list');
    if (!grid) return;
    
    let html = uploadedFotoPrima.map((photo, i) => 
        '<div class="photo-item"><img src="' + photo.data + '"><button class="photo-remove" onclick="event.stopPropagation();removeFotoPrima(' + i + ')"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button></div>'
    ).join('');
    
    html += '<div class="photo-item photo-add-placeholder" onclick="document.getElementById(\'foto-prima-input\').click()"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><line x1="12" y1="8" x2="12" y2="16"/><line x1="8" y1="12" x2="16" y2="12"/></svg><span>Aggiungi</span></div>';
    
    grid.innerHTML = html;
}

function handleFotoDopoUpload(event) {
    const files = event.target.files;
    if (!files) return;
    
    Array.from(files).forEach(file => {
        if (file.type.startsWith('image/')) {
            const reader = new FileReader();
            reader.onload = e => {
                uploadedFotoDopo.push({ data: e.target.result, name: file.name });
                renderFotoDopoGrid();
            };
            reader.readAsDataURL(file);
        }
    });
    event.target.value = '';
}

function removeFotoDopo(index) {
    uploadedFotoDopo.splice(index, 1);
    renderFotoDopoGrid();
}

function renderFotoDopoGrid() {
    const grid = document.getElementById('foto-dopo-list');
    if (!grid) return;
    
    let html = uploadedFotoDopo.map((photo, i) => 
        '<div class="photo-item"><img src="' + photo.data + '"><button class="photo-remove" onclick="event.stopPropagation();removeFotoDopo(' + i + ')"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg></button></div>'
    ).join('');
    
    html += '<div class="photo-item photo-add-placeholder" onclick="document.getElementById(\'foto-dopo-input\').click()"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><line x1="12" y1="8" x2="12" y2="16"/><line x1="8" y1="12" x2="16" y2="12"/></svg><span>Aggiungi</span></div>';
    
    grid.innerHTML = html;
}
