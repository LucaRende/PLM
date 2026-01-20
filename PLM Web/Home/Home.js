    /* ========================================
   HOME - PLM 2
   Logica dashboard CRM e Gantt
   ======================================== */
        // ===== ATTIVITA CRM DASHBOARD =====
        (function() {
            const sb = window.supabase.createClient(
                'https://uoykvjxerdrthnmnfmgc.supabase.co',
                'sb_publishable_iGVhzkLqAktDZmpccXl7OA_PZ2nUYSY'
            );

            let loaded = false;
            let allActivities = [];
            let filteredActivities = [];

            window.toggleDash = function() {
                const header = document.getElementById('dashHeader');
                const content = document.getElementById('dashContent');
                
                // Forza reflow per iOS - fix animazioni ripetute
                void header.offsetHeight;
                void content.offsetHeight;
                
                header.classList.toggle('active');
                content.classList.toggle('show');
                
                // Forza re-render dopo transizione
                setTimeout(() => {
                    void content.offsetHeight;
                }, 350);
            };

            window.toggleFilters = function() {
                const filtersBox = document.getElementById('filtersBox');
                filtersBox.classList.toggle('active');
            };

            window.toggleAct = function(id) {
                const card = document.querySelector(`[data-id="${id}"]`);
                card.classList.toggle('active');
            };

            window.applyFilters = function() {
                const cliente = document.getElementById('filterCliente').value;
                const tipologia = document.getElementById('filterTipologia').value;
                const stato = document.getElementById('filterStato').value;
                const scadenza = document.getElementById('filterScadenza').value;

                filteredActivities = allActivities.filter(a => {
                    if (cliente && a.clienteApplicato !== cliente) return false;
                    if (tipologia && a.tipologiaFormattata !== tipologia) return false;
                    if (stato && a.stato !== stato) return false;
                    if (scadenza) {
                        const urgenza = getUrgencyStatus(a._dataConsegna);
                        if (scadenza !== urgenza) return false;
                    }
                    return true;
                });

                currentPage = 1; // Reset alla prima pagina
                renderActivities(filteredActivities);
            };

            window.resetFilters = function() {
                document.getElementById('filterCliente').value = '';
                document.getElementById('filterTipologia').value = '';
                document.getElementById('filterStato').value = '';
                document.getElementById('filterScadenza').value = '';
                filteredActivities = allActivities;
                currentPage = 1; // Reset alla prima pagina
                renderActivities(filteredActivities);
            };

            function populateFilters(data) {
                const clienti = [...new Set(data.map(a => a.clienteApplicato).filter(Boolean))].sort();
                const tipologie = [...new Set(data.map(a => a.tipologiaFormattata).filter(Boolean))].sort();
                const stati = [...new Set(data.map(a => a.stato).filter(Boolean))].sort();

                const filterCliente = document.getElementById('filterCliente');
                const filterTipologia = document.getElementById('filterTipologia');
                const filterStato = document.getElementById('filterStato');

                clienti.forEach(c => {
                    const option = document.createElement('option');
                    option.value = c;
                    option.textContent = c;
                    filterCliente.appendChild(option);
                });

                tipologie.forEach(t => {
                    const option = document.createElement('option');
                    option.value = t;
                    option.textContent = t;
                    filterTipologia.appendChild(option);
                });

                stati.forEach(s => {
                    const option = document.createElement('option');
                    option.value = s;
                    option.textContent = s;
                    filterStato.appendChild(option);
                });
            }

            function setCircularProgress(elementId, percentId, percentage) {
                const circle = document.getElementById(elementId);
                const percentText = document.getElementById(percentId);
                const circumference = 301.59;
                const offset = circumference - (percentage / 100) * circumference;
                
                setTimeout(() => {
                    circle.style.strokeDashoffset = offset;
                    percentText.textContent = Math.round(percentage) + '%';
                }, 200);
            }

            function calculateDelayPercentages(data) {
                const today = new Date();
                today.setHours(0, 0, 0, 0);

                let preventiviTotal = 0;
                let preventiviRitardo = 0;
                let ordiniTotal = 0;
                let ordiniRitardo = 0;

                data.forEach(a => {
                    const consegna = a._dataConsegna ? new Date(a._dataConsegna) : null;
                    if (!consegna) return;

                    const tipo = (a.tipologiaFormattata || '').toLowerCase();
                    const stato = (a.stato || '').toLowerCase();
                    
                    if (stato.includes('completato') || stato.includes('chiuso')) return;

                    const isRitardo = consegna < today;

                    if (tipo.includes('preventivo')) {
                        preventiviTotal++;
                        if (isRitardo) preventiviRitardo++;
                    } else if (tipo.includes('ordine') || tipo.includes('ordini')) {
                        ordiniTotal++;
                        if (isRitardo) ordiniRitardo++;
                    }
                });

                const preventiviPerc = preventiviTotal > 0 ? (preventiviRitardo / preventiviTotal) * 100 : 0;
                const ordiniPerc = ordiniTotal > 0 ? (ordiniRitardo / ordiniTotal) * 100 : 0;

                return { preventiviPerc, ordiniPerc };
            }

            function getUrgencyStatus(dataConsegna) {
                if (!dataConsegna) return 'futuro';
                
                const today = new Date();
                today.setHours(0, 0, 0, 0);
                
                const consegna = new Date(dataConsegna);
                consegna.setHours(0, 0, 0, 0);
                
                if (consegna < today) return 'scaduto';
                if (consegna.getTime() === today.getTime()) return 'oggi';
                return 'futuro';
            }

            function getStatus(s) {
                if (!s) return 'status-aperto';
                const l = s.toLowerCase();
                if (l.includes('completato') || l.includes('chiuso')) return 'status-completato';
                if (l.includes('corso')) return 'status-in-corso';
                if (l.includes('annullato')) return 'status-annullato';
                return 'status-aperto';
            }

            function fmtDate(d) {
                if (!d) return 'N/D';
                return new Date(d).toLocaleDateString('it-IT', { 
                    day: '2-digit', 
                    month: '2-digit', 
                    year: 'numeric' 
                });
            }

            let currentPage = 1;
            const itemsPerPage = 5;
            let animationDirection = 'right';
            
            function renderActivities(data) {
                const list = document.getElementById('activitiesList');
                const paginationControls = document.getElementById('paginationControls');
                
                if (!data || !data.length) {
                    list.innerHTML = '<div class="empty-state"><div class="empty-icon">📭</div>Nessuna attività trovata</div>';
                    paginationControls.style.display = 'none';
                    return;
                }
                
                const totalPages = Math.ceil(data.length / itemsPerPage);
                const startIndex = (currentPage - 1) * itemsPerPage;
                const endIndex = startIndex + itemsPerPage;
                const currentData = data.slice(startIndex, endIndex);
                
                // Aggiorna info paginazione
                document.getElementById('currentPage').textContent = currentPage;
                document.getElementById('totalPages').textContent = totalPages;
                document.getElementById('prevPage').disabled = currentPage === 1;
                document.getElementById('nextPage').disabled = currentPage === totalPages;
                
                // Mostra controlli se ci sono più pagine
                paginationControls.style.display = totalPages > 1 ? 'flex' : 'none';

                list.innerHTML = currentData.map((a, index) => {
                    const urgencyClass = getUrgencyStatus(a._dataConsegna);
                    const animClass = animationDirection === 'right' ? 'slide-in-right' : 'slide-in-left';
                    const delay = index * 0.05;
                    
                    return `
                    <div class="activity-card ${animClass}" data-id="${a.ID_Attivita}" style="animation-delay: ${delay}s;">
                        <div class="activity-card-header ${urgencyClass}" onclick="toggleAct(${a.ID_Attivita})">
                            <div class="activity-info">
                                <div class="activity-title">
                                    #${a.ID_Attivita} · ${a.clienteApplicato || 'Cliente non specificato'}
                                </div>
                                <div class="activity-meta">
                                    <div class="meta-item">
                                        <span class="meta-label">📅</span>
                                        <span>${a.dataCreazioneFormattata || fmtDate(a._dataCreazione)}</span>
                                    </div>
                                    <div class="meta-item">
                                        <span class="meta-label">🎯</span>
                                        <span>${a.dataConsegnaFormattata || fmtDate(a._dataConsegna)}</span>
                                    </div>
                                    <div class="meta-item">
                                        <span class="meta-label">📦</span>
                                        <span>${a.tipologiaFormattata || 'Non specificato'}</span>
                                    </div>
                                </div>
                                <div class="activity-footer">
                                    <span class="status-badge ${getStatus(a.stato)}">${a.stato || 'Aperto'}</span>
                                    <span class="activity-arrow">▼</span>
                                </div>
                            </div>
                        </div>
                        <div class="activity-details">
                            <div class="details-wrapper">
                                <div class="details-grid">
                                    <div class="detail-box">
                                        <div class="detail-label">Cliente</div>
                                        <div class="detail-value">${a.clienteApplicato || 'Non specificato'}</div>
                                    </div>
                                    <div class="detail-box">
                                        <div class="detail-label">Tipologia</div>
                                        <div class="detail-value">${a.tipologiaFormattata || 'Non specificato'}</div>
                                    </div>
                                    <div class="detail-box">
                                        <div class="detail-label">Data Creazione</div>
                                        <div class="detail-value">${a.dataCreazioneFormattata || fmtDate(a._dataCreazione)}</div>
                                    </div>
                                    <div class="detail-box">
                                        <div class="detail-label">Data Consegna</div>
                                        <div class="detail-value">${a.dataConsegnaFormattata || fmtDate(a._dataConsegna)}</div>
                                    </div>
                                    <div class="detail-box">
                                        <div class="detail-label">Trasferimento</div>
                                        <div class="detail-value">${a.trasferimento || 'Non specificato'}</div>
                                    </div>
                                    <div class="detail-box">
                                        <div class="detail-label">Stato</div>
                                        <div class="detail-value">${a.stato || 'Aperto'}</div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                `}).join('');
            }
            
            window.changePage = function(direction) {
                const totalPages = Math.ceil(filteredActivities.length / itemsPerPage);
                const newPage = currentPage + direction;
                
                if (newPage < 1 || newPage > totalPages) return;
                
                // Imposta direzione animazione
                animationDirection = direction > 0 ? 'right' : 'left';
                
                // Animazione uscita
                const cards = document.querySelectorAll('.activity-card');
                const outClass = direction > 0 ? 'slide-out-left' : 'slide-out-right';
                cards.forEach((card, index) => {
                    card.style.animationDelay = `${index * 0.03}s`;
                    card.classList.add(outClass);
                });
                
                // Dopo animazione uscita, cambia pagina
                setTimeout(() => {
                    currentPage = newPage;
                    renderActivities(filteredActivities);
                    
                    // Scroll alla prima attività della nuova pagina
                    setTimeout(() => {
                        const firstCard = document.querySelector('.activity-card');
                        if (firstCard) {
                            firstCard.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
                        }
                    }, 50);
                }, 300);
            };

            async function loadData() {
                const list = document.getElementById('activitiesList');
                list.innerHTML = '<div class="loading-state">Caricamento...</div>';

                try {
                    const { data, error } = await sb.from('Attivita').select('*');

                    if (error) throw error;

                    if (data && data.length > 0) {
                        const { preventiviPerc, ordiniPerc } = calculateDelayPercentages(data);
                        setCircularProgress('progressPreventivi', 'percentPreventivi', preventiviPerc);
                        setCircularProgress('progressOrdini', 'percentOrdini', ordiniPerc);
                        
                        const today = new Date();
                        today.setHours(0, 0, 0, 0);
                        
                        data.sort((a, b) => {
                            const dataA = a._dataConsegna ? new Date(a._dataConsegna) : new Date('9999-12-31');
                            const dataB = b._dataConsegna ? new Date(b._dataConsegna) : new Date('9999-12-31');
                            return dataA - dataB;
                        });

                        allActivities = data;
                        filteredActivities = data;
                        
                        populateFilters(data);
                        renderActivities(data);
                    } else {
                        list.innerHTML = '<div class="empty-state"><div class="empty-icon">📭</div>Nessuna attività</div>';
                    }

                } catch (e) {
                    list.innerHTML = `<div class="empty-state"><div class="empty-icon">⚠️</div>Errore: ${e.message}</div>`;
                }
            }

            // Event listener semplice che funziona sia su desktop che iPhone
            const dashHeader = document.getElementById('dashHeader');
            dashHeader.addEventListener('click', window.toggleDash);
            
            window.loadCRMData = loadData; // Esposto per initializeDashboard
        })();

        // ===== UFFICIO TECNICO GANTT DASHBOARD =====
        (function() {
            const SUPABASE_URL = 'https://uoykvjxerdrthnmnfmgc.supabase.co';
            const SUPABASE_KEY = 'sb_publishable_iGVhzkLqAktDZmpccXl7OA_PZ2nUYSY';
            const sb = supabase.createClient(SUPABASE_URL, SUPABASE_KEY);

            let allTasks = [];

            function toggleGanttDashboard() {
                const header = document.getElementById('ganttHeader');
                const container = document.getElementById('ganttContainer');
                
                // Forza reflow per iOS - fix animazioni ripetute
                void header.offsetHeight;
                void container.offsetHeight;
                
                header.classList.toggle('active');
                container.classList.toggle('collapsed');
                
                // Forza re-render dopo transizione
                setTimeout(() => {
                    void container.offsetHeight;
                }, 350);
            }

            function generateTimeline() {
                const timelineHeader = document.getElementById('ganttTimelineHeader');
                const hours = ['08', '09', '10', '11', '12', '13', '14', '15', '16', '17'];
                
                timelineHeader.innerHTML = hours.map(h => 
                    `<div class="gantt-hour-header">${h}</div>`
                ).join('');
            }

            function calculateTaskPosition(oraInizio, oraFine) {
                const startHour = 8;
                const endHour = 18;
                const totalHours = endHour - startHour;
                
                // Parse time format: "11.00.00" or "11:00:00" (dots or colons)
                const parseTime = (timeStr) => {
                    if (!timeStr) return 0;
                    
                    const str = String(timeStr).trim();
                    // Split by dots OR colons
                    const parts = str.split(/[.:]/).map(Number);
                    const hours = parts[0] || 0;
                    const minutes = parts[1] || 0;
                    
                    return hours + (minutes / 60);
                };
                
                const startDecimal = parseTime(oraInizio);
                const endDecimal = parseTime(oraFine);
                
                const left = ((startDecimal - startHour) / totalHours) * 100;
                const width = ((endDecimal - startDecimal) / totalHours) * 100;
                
                return { left: Math.max(0, left), width: Math.max(0, width) };
            }

            function isUrgent(dataConsegna) {
                if (!dataConsegna) return false;
                const today = new Date();
                const delivery = new Date(dataConsegna);
                return today.toDateString() === delivery.toDateString();
            }

            function formatTime(time) {
                if (!time) return 'N/D';
                // Convert dots to colons for display: 11.00.00 → 11:00
                const cleaned = String(time).replace(/\./g, ':');
                return cleaned.substring(0, 5); // HH:MM
            }

            function renderGantt(data) {
                const ganttSummaryRows = document.getElementById('ganttSummaryRows');
                const ganttDetailsRows = document.getElementById('ganttDetailsRows');
                
                // Fixed list of operators
                const operators = [
                    "Alessandro Colio",
                    "Caterina Dello Stritto",
                    "Christian D'Angelo",
                    "Federico Cavazza",
                    "Marco Garzesi"
                ];
                
                if (!data || data.length === 0) {
                    // Show all operators even with no tasks
                    renderEmptyGantt(operators);
                    ganttDetailsRows.innerHTML = `
                        <div class="empty-state">
                            <div class="empty-icon">📋</div>
                            <div class="empty-text">Nessun lavoro programmato</div>
                        </div>
                    `;
                    return;
                }

                // Group tasks by person - CORRECTLY
                const byPerson = {};
                operators.forEach(op => {
                    byPerson[op] = [];
                });
                
                // Add each task to its assigned operator
                data.forEach(task => {
                    const person = task.Persona;
                    console.log('Task:', task.Descrizione, 'assigned to:', person); // Debug
                    
                    // Check if this operator exists in our list
                    if (person && byPerson[person] !== undefined) {
                        byPerson[person].push(task);
                    } else {
                        console.warn('Operator not found in list:', person); // Debug
                    }
                });

                // Debug: show what each operator has
                console.log('Tasks by person:', byPerson);

                // Update stats
                // Persone = numero totale di operatori
                document.getElementById('totalPersons').textContent = operators.length;
                document.getElementById('totalTasks').textContent = data.length;
                
                // Occupati = numero di operatori che hanno almeno una lavorazione
                const operatorsWithTasks = operators.filter(op => byPerson[op].length > 0).length;
                document.getElementById('avgOccupation').textContent = operatorsWithTasks;

                // Calculate current time position for the indicator
                const now = new Date();
                const currentHour = now.getHours() + (now.getMinutes() / 60);
                const startHour = 8;
                const endHour = 18;
                let currentTimePercent = null;
                
                if (currentHour >= startHour && currentHour < endHour) {
                    currentTimePercent = ((currentHour - startHour) / (endHour - startHour)) * 100;
                }

                // Render Summary View (collapsed state)
                ganttSummaryRows.innerHTML = operators.map(person => {
                    const tasks = byPerson[person] || [];
                    
                    // Create 10 hour slots (8-17)
                    const slots = Array(10).fill(null).map((_, i) => 
                        `<div class="gantt-hour-slot"></div>`
                    ).join('');

                    // Create task bars for THIS person's tasks - NO OVERLAP
                    const taskBars = tasks.map(task => {
                        const { left, width } = calculateTaskPosition(task.OraInizio, task.OraFine);
                        const urgent = isUrgent(task.DataConsegna);
                        
                        return `
                            <div class="gantt-task-bar ${urgent ? 'urgente' : ''}" 
                                 style="left: ${left}%; width: ${width}%;"
                                 title="${task.Descrizione} (${formatTime(task.OraInizio)} - ${formatTime(task.OraFine)})"></div>
                        `;
                    }).join('');
                    // Add current time line if within working hours
                    const currentTimeLine = currentTimePercent !== null 
                        ? `<div class="current-time-line" style="left: ${currentTimePercent}%;"></div>` 
                        : '';

                    return `
                        <div class="gantt-summary-row">
                            <div class="gantt-row-header">
                                <div class="gantt-person-cell">${person}</div>
                            </div>
                            <div class="gantt-row-timeline">
                                <div class="gantt-timeline-wrapper">
                                    ${slots}
                                    ${taskBars}
                                    ${currentTimeLine}
                                </div>
                            </div>
                        </div>
                    `;
                }).join('');

                // Render Detail View (expanded state)
                ganttDetailsRows.innerHTML = operators.map(person => {
                    const tasks = byPerson[person] || [];
                    
                    if (tasks.length === 0) {
                        return `
                            <div class="person-section">
                                <div class="person-section-header">
                                    <h3>${person}</h3>
                                    <span class="task-count">0 lavori</span>
                                </div>
                                <div class="task-list">
                                    <div class="empty-state" style="padding: 20px;">
                                        <div class="empty-text" style="font-size: 0.85em;">Nessun lavoro assegnato</div>
                                    </div>
                                </div>
                            </div>
                        `;
                    }
                    
                    // Sort tasks by start time
                    tasks.sort((a, b) => {
                        const timeA = a.OraInizio || '00:00:00';
                        const timeB = b.OraInizio || '00:00:00';
                        return timeA.localeCompare(timeB);
                    });
                    
                    const taskList = tasks.map(task => {
                        const urgent = isUrgent(task.DataConsegna);
                        return `
                            <div class="task-card">
                                <div class="task-header">
                                    <div class="task-id">#${task.ID || '?'}</div>
                                    <div class="task-time">${formatTime(task.OraInizio)} - ${formatTime(task.OraFine)}</div>
                                </div>
                                <div class="task-description">${task.Descrizione || 'Nessuna descrizione'}</div>
                                <div class="task-footer">
                                    <div class="task-badge ${urgent ? 'priority-high' : 'priority-normal'}">
                                        ${urgent ? '🔥 Urgente' : '📅 Normale'}
                                    </div>
                                    <div class="task-badge status">
                                        ✓ ${task.Stato || 'In corso'}
                                    </div>
                                </div>
                            </div>
                        `;
                    }).join('');

                    return `
                        <div class="person-section">
                            <div class="person-section-header">
                                <h3>${person}</h3>
                                <span class="task-count">${tasks.length} ${tasks.length === 1 ? 'lavoro' : 'lavori'}</span>
                            </div>
                            <div class="task-list">
                                ${taskList}
                            </div>
                        </div>
                    `;
                }).join('');
            }

            function renderEmptyGantt(operators) {
                const ganttSummaryRows = document.getElementById('ganttSummaryRows');
                
                // Calculate current time position
                const now = new Date();
                const currentHour = now.getHours() + (now.getMinutes() / 60);
                const startHour = 8;
                const endHour = 18;
                let currentTimePercent = null;
                
                if (currentHour >= startHour && currentHour < endHour) {
                    currentTimePercent = ((currentHour - startHour) / (endHour - startHour)) * 100;
                }
                
                document.getElementById('totalPersons').textContent = '0';
                document.getElementById('totalTasks').textContent = '0';
                document.getElementById('avgOccupation').textContent = '0';

                ganttSummaryRows.innerHTML = operators.map(person => {
                    const slots = Array(10).fill(null).map((_, i) => 
                        `<div class="gantt-hour-slot"></div>`
                    ).join('');

                    const currentTimeLine = currentTimePercent !== null 
                        ? `<div class="current-time-line" style="left: ${currentTimePercent}%;"></div>` 
                        : '';

                    return `
                        <div class="gantt-summary-row">
                            <div class="gantt-row-header">
                                <div class="gantt-person-cell">${person}</div>
                            </div>
                            <div class="gantt-row-timeline">
                                <div class="gantt-timeline-wrapper">
                                    ${slots}
                                    ${currentTimeLine}
                                </div>
                            </div>
                        </div>
                    `;
                }).join('');
            }

            async function loadGanttData() {
                const container = document.getElementById('ganttSummaryRows');
                container.innerHTML = `
                    <div class="loading-state">
                        <div class="loading-spinner"></div>
                        <div>Caricamento dati...</div>
                    </div>
                `;

                try {
                    const today = new Date();
                    const todayStr = today.toISOString().split('T')[0];

                    // Get all records from UfficioTecnico
                    const { data, error } = await sb
                        .from('UfficioTecnico')
                        .select('*');

                    if (error) throw error;

                    // Transform the data into individual tasks with REAL times
                    const tasks = [];
                    
                    if (data && data.length > 0) {
                        data.forEach(record => {
                            // Disegno 2D - if it has time slots, show it
                            if (record.Disegno_2D_Assegnato && 
                                record.Operatore_Disegno_2D_Assegnato && 
                                record.Ora_Disegno_2D_Inizio && 
                                record.Ora_Disegno_2D_Fine) {
                                
                                tasks.push({
                                    ID: record.id,
                                    Persona: record.Operatore_Disegno_2D_Assegnato,
                                    Descrizione: `Disegno 2D - ${record.codiceProgetto || 'Progetto'}`,
                                    OraInizio: record.Ora_Disegno_2D_Inizio,
                                    OraFine: record.Ora_Disegno_2D_Fine,
                                    DataConsegna: record.Data_Disegno_2D_Assegnato_FinePrevista,
                                    Stato: record.Disegno_2D_Fatto ? 'Completato' : 'In corso',
                                    Tipo: 'Disegno 2D'
                                });
                            }

                            // Disegno 3D
                            if (record.Disegno_3D_Assegnato && 
                                record.Operatore_Disegno_3D_Assegnato && 
                                record.Ora_Disegno_3D_Inizio && 
                                record.Ora_Disegno_3D_Fine) {
                                
                                tasks.push({
                                    ID: record.id,
                                    Persona: record.Operatore_Disegno_3D_Assegnato,
                                    Descrizione: `Disegno 3D - ${record.codiceProgetto || 'Progetto'}`,
                                    OraInizio: record.Ora_Disegno_3D_Inizio,
                                    OraFine: record.Ora_Disegno_3D_Fine,
                                    DataConsegna: record.Data_Disegno_3D_Assegnato_FinePrevista,
                                    Stato: record.Disegno_3D_Fatto ? 'Completato' : 'In corso',
                                    Tipo: 'Disegno 3D'
                                });
                            }

                            // Distinta
                            if (record.Distinta_Assegnato && 
                                record.Operatore_Distinta_Assegnato && 
                                record.Ora_Distinta_Inizio && 
                                record.Ora_Distinta_Fine) {
                                
                                tasks.push({
                                    ID: record.id,
                                    Persona: record.Operatore_Distinta_Assegnato,
                                    Descrizione: `Distinta - ${record.codiceProgetto || 'Progetto'}`,
                                    OraInizio: record.Ora_Distinta_Inizio,
                                    OraFine: record.Ora_Distinta_Fine,
                                    DataConsegna: record.Data_Distinta_Assegnato_FinePrevista,
                                    Stato: record.Distinta_Fatto ? 'Completato' : 'In corso',
                                    Tipo: 'Distinta'
                                });
                            }

                            // Stampare 2D
                            if (record.Stampare_2D_Assegnato && 
                                record.Operatore_Stampare_2D_Assegnato && 
                                record.Ora_Stampare_2D_Inizio && 
                                record.Ora_Stampare_2D_Fine) {
                                
                                tasks.push({
                                    ID: record.id,
                                    Persona: record.Operatore_Stampare_2D_Assegnato,
                                    Descrizione: `Stampare 2D - ${record.codiceProgetto || 'Progetto'}`,
                                    OraInizio: record.Ora_Stampare_2D_Inizio,
                                    OraFine: record.Ora_Stampare_2D_Fine,
                                    DataConsegna: record.Data_Stampare_2D_Assegnato_FinePrevista,
                                    Stato: record.Stampare_2D_Fatto ? 'Completato' : 'In corso',
                                    Tipo: 'Stampare 2D'
                                });
                            }
                        });
                    }

                    allTasks = tasks;
                    
                    const dateLabel = today.toLocaleDateString('it-IT', { 
                        weekday: 'long', 
                        day: 'numeric', 
                        month: 'long' 
                    });
                    document.getElementById('currentDate').textContent = dateLabel.charAt(0).toUpperCase() + dateLabel.slice(1);

                    renderGantt(allTasks);

                } catch (e) {
                    container.innerHTML = `
                        <div class="empty-state">
                            <div class="empty-icon">⚠️</div>
                            <div class="empty-text">Errore: ${e.message}</div>
                        </div>
                    `;
                    console.error('Errore caricamento:', e);
                }
            }

            function formatHour(decimalHour) {
                const hours = Math.floor(decimalHour);
                const minutes = Math.floor((decimalHour - hours) * 60);
                return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:00`;
            }

            // Event listener semplice che funziona sia su desktop che iPhone
            const ganttHeader = document.getElementById('ganttHeader');
            ganttHeader.addEventListener('click', toggleGanttDashboard);
            
            generateTimeline();
            window.loadGanttData = loadGanttData; // Esposto per initializeDashboard

            setInterval(loadGanttData, 5 * 60 * 1000);

// Esponi funzioni globalmente per initializeDashboard
window.loadData = function() {
    // Trigger il caricamento CRM
    const dashHeader = document.getElementById('dashHeader');
    if (dashHeader) {
        const event = new Event('load');
        document.dispatchEvent(event);
    }
};

