/* ========================================
   AMMINISTRAZIONE - PLM 2
   Logica dashboard Castelletti
   ======================================== */
        // ===== PANORAMICA CASTELLETTI DASHBOARD =====
        (function() {
            const SUPABASE_URL = 'https://uoykvjxerdrthnmnfmgc.supabase.co';
            const SUPABASE_KEY = 'sb_publishable_iGVhzkLqAktDZmpccXl7OA_PZ2nUYSY';
            const sb = supabase.createClient(SUPABASE_URL, SUPABASE_KEY);
            
            const MESI = ['Gennaio', 'Febbraio', 'Marzo', 'Aprile', 'Maggio', 'Giugno', 
                         'Luglio', 'Agosto', 'Settembre', 'Ottobre', 'Novembre', 'Dicembre'];
            
            let allFatture = [];
            let fatturePerBanca = {};
            let castellettiPerBanca = {}; // Castelletti specifici per ogni banca
            
            // Toggle dashboard
            document.getElementById('castellettiHeader').addEventListener('click', function() {
                const header = this;
                const content = document.getElementById('castellettiContent');
                
                void header.offsetHeight;
                header.classList.toggle('active');
                content.classList.toggle('show');
                
                setTimeout(() => void content.offsetHeight, 350);
            });
            
            // Carica dati
            async function loadData() {
                const list = document.getElementById('castellettiList');
                list.innerHTML = '<div class="loading-state">Caricamento castelletti...</div>';
                
                try {
                    // Carica PRIMA i castelletti
                    const { data: bancheData, error: bancheError } = await sb.from('ImportiCastelletti').select('*');
                    if (bancheError) throw bancheError;
                    
                    // Mappa castelletti per nome banca
                    if (bancheData) {
                        bancheData.forEach(b => {
                            castellettiPerBanca[b.nome] = b.totaleCastelletto || 0;
                        });
                    }
                    
                    // Poi carica le fatture
                    const { data, error } = await sb.from('FattureCastelletti').select('*');
                    if (error) throw error;
                    
                    if (data && data.length > 0) {
                        allFatture = data;
                        organizzaDatiPerBanca(data);
                        renderBanche();
                    } else {
                        list.innerHTML = '<div class="empty-state"><div class="empty-icon">📊</div><div class="empty-text">Nessuna fattura trovata</div></div>';
                    }
                } catch (e) {
                    list.innerHTML = `<div class="empty-state"><div class="empty-icon">⚠️</div><div class="empty-text">Errore: ${e.message}</div></div>`;
                }
            }
            
            // Organizza dati per banca e mese
            function organizzaDatiPerBanca(fatture) {
                fatturePerBanca = {};
                
                fatture.forEach(f => {
                    const banca = f.bancaAssociata || 'Banca non specificata';
                    
                    if (!fatturePerBanca[banca]) {
                        fatturePerBanca[banca] = {};
                        MESI.forEach((_, idx) => {
                            fatturePerBanca[banca][idx] = [];
                        });
                    }
                    
                    // Estrai mese dalla data scadenza
                    if (f.dataScadenza) {
                        const date = new Date(f.dataScadenza);
                        const meseIdx = date.getMonth();
                        fatturePerBanca[banca][meseIdx].push(f);
                    }
                });
            }
            
            // Render lista banche
            function renderBanche() {
                const list = document.getElementById('castellettiList');
                const banche = Object.keys(fatturePerBanca).sort();
                
                list.innerHTML = banche.map((banca, idx) => {
                    const castelletto = castellettiPerBanca[banca] || 0;
                    
                    return `
                    <div class="banca-card">
                        <div class="banca-header" onclick="toggleBanca(${idx})">
                            <div style="display: flex; align-items: center; gap: 16px; flex: 1;">
                                <svg width="32" height="32" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
                                    <path d="M3 9L12 2L21 9V20C21 20.5304 20.7893 21.0391 20.4142 21.4142C20.0391 21.7893 19.5304 22 19 22H5C4.46957 22 3.96086 21.7893 3.58579 21.4142C3.21071 21.0391 3 20.5304 3 20V9Z" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                                    <path d="M9 22V12H15V22" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                                </svg>
                                <div class="banca-name">${banca}</div>
                            </div>
                            <div style="display: flex; align-items: center; gap: 16px;">
                                <div style="text-align: right;">
                                    <div style="font-size: 0.75em; color: rgba(255,255,255,0.6); margin-bottom: 2px;">Castelletto</div>
                                    <div style="font-size: 1.1em; font-weight: 700; color: #22c55e;">${formatEuro(castelletto)}</div>
                                </div>
                                <span class="banca-arrow" id="bancaArrow${idx}">▼</span>
                            </div>
                        </div>
                        <div class="banca-content" id="bancaContent${idx}">
                            <div class="mesi-grid" id="mesiGrid${idx}"></div>
                        </div>
                    </div>
                `}).join('');
            }
            
            // Toggle banca
            window.toggleBanca = function(bancaIdx) {
                const header = document.querySelector(`.banca-card:nth-child(${bancaIdx + 1}) .banca-header`);
                const content = document.getElementById(`bancaContent${bancaIdx}`);
                const arrow = document.getElementById(`bancaArrow${bancaIdx}`);
                
                const isOpen = header.classList.contains('active');
                
                if (!isOpen) {
                    // Apri e carica mesi
                    header.classList.add('active');
                    content.classList.add('show');
                    
                    const banca = Object.keys(fatturePerBanca).sort()[bancaIdx];
                    renderMesi(bancaIdx, banca);
                } else {
                    // Chiudi
                    header.classList.remove('active');
                    content.classList.remove('show');
                }
            };
            
            // Render mesi per una banca
            function renderMesi(bancaIdx, banca) {
                const grid = document.getElementById(`mesiGrid${bancaIdx}`);
                
                // Prendi castelletto specifico per questa banca
                const CASTELLETTO_TOTALE = castellettiPerBanca[banca] || 0;
                
                // Calcola totale da pagare per tutta la banca
                const tutteFattureBanca = Object.values(fatturePerBanca[banca]).flat();
                const totaleDaPagare = tutteFattureBanca.reduce((sum, f) => sum + (f.importoDaPagare || 0), 0);
                
                grid.innerHTML = MESI.map((mese, meseIdx) => {
                    const fatture = fatturePerBanca[banca][meseIdx] || [];
                    
                    // Somma da pagare del mese
                    const daPagareMese = fatture.reduce((sum, f) => sum + (f.importoDaPagare || 0), 0);
                    
                    // Castelletto occupato = X - da pagare mese
                    const occupato = totaleDaPagare - daPagareMese;
                    
                    // Castelletto rimanente = castelletto totale + occupato
                    const rimanente = CASTELLETTO_TOTALE + occupato;
                    
                    // Percentuale basata su quanto è occupato rispetto al totale
                    const percentuale = CASTELLETTO_TOTALE > 0 
                        ? Math.min(100, Math.max(0, ((CASTELLETTO_TOTALE - rimanente) / CASTELLETTO_TOTALE) * 100))
                        : 0;
                    
                    let badgeClass = 'green';
                    let badgeText = 'Disponibile';
                    if (percentuale > 80) {
                        badgeClass = 'red';
                        badgeText = 'Critico';
                    } else if (percentuale > 50) {
                        badgeClass = 'yellow';
                        badgeText = 'Attenzione';
                    }
                    
                    return `
                        <div class="mese-card" id="meseCard_${bancaIdx}_${meseIdx}" onclick="toggleMese(${bancaIdx}, ${meseIdx}, '${banca}')">
                            <div class="mese-nome">${mese}</div>
                            <svg class="mese-progress" viewBox="0 0 100 100">
                                <circle cx="50" cy="50" r="40" fill="none" stroke="rgba(255,255,255,0.1)" stroke-width="8"/>
                                <circle cx="50" cy="50" r="40" fill="none" 
                                    stroke="${percentuale > 80 ? '#ef4444' : percentuale > 50 ? '#f59e0b' : '#22c55e'}"
                                    stroke-width="8" stroke-linecap="round"
                                    stroke-dasharray="${Math.PI * 80}" 
                                    stroke-dashoffset="${Math.PI * 80 * (1 - percentuale / 100)}"
                                    transform="rotate(-90 50 50)"/>
                                <text x="50" y="50" text-anchor="middle" dy="0.3em" fill="white" font-size="20" font-weight="700">
                                    ${percentuale.toFixed(0)}%
                                </text>
                            </svg>
                            <div class="mese-stats">
                                <div class="mese-stats-row">
                                    <span>Occupato:</span>
                                    <span class="mese-stats-value">${formatEuroShort(Math.abs(occupato))}</span>
                                </div>
                                <div class="mese-stats-row">
                                    <span>Disponibile:</span>
                                    <span class="mese-stats-value">${formatEuroShort(Math.abs(rimanente))}</span>
                                </div>
                                <div class="mese-stats-row">
                                    <span>Fatture:</span>
                                    <span class="mese-stats-value">${fatture.length}</span>
                                </div>
                            </div>
                            <div class="mese-badge ${badgeClass}">${badgeText}</div>
                        </div>
                    `;
                }).join('');
                
                // Aggiungi div per fatture sotto la griglia
                grid.insertAdjacentHTML('afterend', '<div id="fattureContainer' + bancaIdx + '"></div>');
            }
            
            // Toggle mese per mostrare fatture (SOLO UNO APERTO ALLA VOLTA)
            window.toggleMese = function(bancaIdx, meseIdx, banca) {
                const container = document.getElementById(`fattureContainer${bancaIdx}`);
                const currentMeseId = `fatture_${bancaIdx}_${meseIdx}`;
                
                // Se già aperto, chiudi
                if (container.dataset.currentMese === currentMeseId) {
                    container.innerHTML = '';
                    container.dataset.currentMese = '';
                    return;
                }
                
                // CHIUDI TUTTI gli altri mesi aperti in TUTTE le banche
                document.querySelectorAll('[id^="fattureContainer"]').forEach(cont => {
                    if (cont !== container) {
                        cont.innerHTML = '';
                        cont.dataset.currentMese = '';
                    }
                });
                
                // Mostra fatture del mese
                const fatture = fatturePerBanca[banca][meseIdx] || [];
                container.dataset.currentMese = currentMeseId;
                
                if (fatture.length === 0) {
                    container.innerHTML = '<div class="fatture-list"><div style="text-align: center; color: rgba(255,255,255,0.5); padding: 20px;">Nessuna fattura in questo mese</div></div>';
                } else {
                    container.innerHTML = `
                        <div class="fatture-list">
                            <h4 style="color: white; margin-bottom: 12px; padding: 0 0 8px; border-bottom: 1px solid rgba(255,255,255,0.1);">
                                Fatture ${MESI[meseIdx]} (${fatture.length})
                            </h4>
                            ${fatture.map(f => `
                                <div class="fattura-item">
                                    <div class="fattura-field">
                                        <div class="fattura-label">Cliente</div>
                                        <div class="fattura-value">${f.cliente || '-'}</div>
                                    </div>
                                    <div class="fattura-field">
                                        <div class="fattura-label">N° Fattura</div>
                                        <div class="fattura-value">${f.numeroFattura || '-'}</div>
                                    </div>
                                    <div class="fattura-field">
                                        <div class="fattura-label">Tipologia</div>
                                        <div class="fattura-value">${f.tipologiaFattura || '-'}</div>
                                    </div>
                                    <div class="fattura-field">
                                        <div class="fattura-label">Creazione</div>
                                        <div class="fattura-value">${f.dataCreazione_Formattato || '-'}</div>
                                    </div>
                                    <div class="fattura-field">
                                        <div class="fattura-label">Scadenza</div>
                                        <div class="fattura-value">${f.dataScadenzaFormattato || '-'}</div>
                                    </div>
                                    <div class="fattura-field">
                                        <div class="fattura-label">Da Pagare</div>
                                        <div class="fattura-value" style="color: ${f.importoDaPagare < 0 ? '#22c55e' : '#ef4444'}">
                                            ${f.importoDaPagare_Formattato || '-'}
                                        </div>
                                    </div>
                                </div>
                            `).join('')}
                        </div>
                    `;
                }
                
                // AUTO-SCROLL al container delle fatture dopo breve delay per animazione
                setTimeout(() => {
                    container.scrollIntoView({ 
                        behavior: 'smooth', 
                        block: 'nearest'
                    });
                }, 100);
            };
            
            // Format euro
            function formatEuro(value) {
                return new Intl.NumberFormat('it-IT', { 
                    style: 'currency', 
                    currency: 'EUR',
                    minimumFractionDigits: 0,
                    maximumFractionDigits: 0
                }).format(Math.abs(value));
            }
            
            // Format euro short (con K)
            function formatEuroShort(value) {
                const abs = Math.abs(value);
                if (abs >= 1000) {
                    return (abs / 1000).toFixed(0) + 'K€';
                }
                return abs.toFixed(0) + '€';
            }
            
            // Carica dati all'avvio
            window.loadCastellettiData = loadData; // Esposto per initializeDashboard
        })();
        
        // Controllo accesso admin
        window.addEventListener('DOMContentLoaded', function() {
            const accessLevel = localStorage.getItem('plm_access_level');
            if (accessLevel !== 'admin') {
                alert('Accesso negato. Solo gli amministratori possono accedere a questa sezione.');
                window.location.href = 'index.html';
            }
        });
        
        // Funzione per scroll ai cruscotti
        function scrollToDashboard(headerId) {
            const header = document.getElementById(headerId);
            if (!header) return;
            
            // Se il cruscotto è chiuso, aprilo prima
            const isActive = header.classList.contains('active');
            if (!isActive) {
                header.click();
                
                // Dopo l'apertura, scrolla
                setTimeout(() => {
                    header.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }, 350);
            } else {
                // Se già aperto, scrolla direttamente
                header.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        }
        
        // Rendi la funzione accessibile globalmente
        window.scrollToDashboard = scrollToDashboard;
        
        // Aggiorna visibilità voci menu in base al livello accesso
        function updateMenuVisibility() {
            const adminMenuItem = document.getElementById('adminMenuItem');
            if (adminMenuItem) {
                adminMenuItem.style.display = userAccessLevel === 'admin' ? 'flex' : 'none';
            }
        }
        
        // Logout
        function logout() {
            localStorage.removeItem('plm_remembered_password');
            localStorage.removeItem('plm_access_level');
            location.reload();
        }
        
        window.logout = logout;
        
        // Menu floating toggle
        function toggleMenu() {
            const menuPanel = document.getElementById('menuPanel');
            const menuToggle = document.getElementById('menuToggle');
            menuPanel.classList.toggle('active');
            menuToggle.classList.toggle('active');
        }
        
        // Chiudi menu quando clicchi fuori
        document.addEventListener('click', function(e) {
            const menu = document.querySelector('.floating-menu');
            if (menu && !menu.contains(e.target)) {
                document.getElementById('menuPanel').classList.remove('active');
                document.getElementById('menuToggle').classList.remove('active');
            }
        });
    
    <!-- Menu Floating Stile Notion -->
    <div class="floating-menu">
        <button class="menu-toggle" id="menuToggle" onclick="toggleMenu()">
            <svg viewBox="0 0 24 24" fill="none" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="1"></circle>
                <circle cx="12" cy="5" r="1"></circle>
                <circle cx="12" cy="19" r="1"></circle>
            </svg>
        </button>
        
        <div class="menu-panel" id="menuPanel">
            <a href="index.html" class="menu-item">
                <div class="menu-item-icon">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <rect x="3" y="3" width="7" height="7" rx="1"></rect>
                        <rect x="14" y="3" width="7" height="7" rx="1"></rect>
                        <rect x="14" y="14" width="7" height="7" rx="1"></rect>
                        <rect x="3" y="14" width="7" height="7" rx="1"></rect>
                    </svg>
                </div>
                <span class="menu-item-text">Dashboard principale</span>
            </a>
            
            <a href="Amministrazione.html" class="menu-item current" id="adminMenuItem" style="display: block;">
                <div class="menu-item-icon">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="3"></circle>
                        <path d="M12 1v6m0 6v6"></path>
                        <circle cx="12" cy="12" r="10"></circle>
                    </svg>
                </div>
                <span class="menu-item-text">Amministrazione</span>
            </a>
            
            <div style="border-top: 1px solid rgba(255, 255, 255, 0.1); margin: 8px 0;"></div>
            
            <a href="#" class="menu-item" onclick="logout(); return false;">
                <div class="menu-item-icon">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"></path>
                        <polylin
