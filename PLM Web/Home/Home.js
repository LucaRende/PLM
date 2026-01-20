/* ========================================
   HOME - PLM 2
   Carica e gestisce le dashboard Home
   ======================================== */

// Supabase config
const SUPABASE_URL = 'https://uoykvjxerdrthnmnfmgc.supabase.co';
const SUPABASE_ANON_KEY = 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InVveWt2anhldXJydGhubW5mbWdjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3MzEyNTI5NTUsImV4cCI6MjA0NjgyODk1NX0.dOhVPaOLEolHKV0Wf76QSgp8M75BC4yzrlCVfj7oP2E';

let supabaseClient = null;
let allData = [];
let filteredData = [];
let currentPage = 1;
const itemsPerPage = 5;

/**
 * Inizializza Supabase
 */
function initSupabase() {
    if (!supabaseClient) {
        supabaseClient = supabase.createClient(SUPABASE_URL, SUPABASE_ANON_KEY);
    }
    return supabaseClient;
}

/**
 * Carica contenuto Home
 */
async function loadHomeContent() {
    const container = document.getElementById('content-container');
    if (!container) return;
    
    const basePath = window.BASE_PATH;
    const filePath = basePath + '/Home/Home.html';
    
    try {
        const response = await fetch(filePath);
        const html = await response.text();
        
        const parser = new DOMParser();
        const doc = parser.parseFromString(html, 'text/html');
        
        const template = doc.getElementById('plm-home');
        if (template) {
            container.innerHTML = template.innerHTML;
            
            // Inizializza dashboard
            initSupabase();
            renderGanttTimelineHeader();
            loadData();
            loadGanttData();
        }
    } catch (error) {
        console.error('Errore caricamento home:', error);
    }
}

/* ========================================
   DASHBOARD CRM
   ======================================== */

/**
 * Toggle dashboard CRM
 */
function toggleDash() {
    const header = document.getElementById('dashHeader');
    const content = document.getElementById('dashContent');
    
    if (header && content) {
        header.classList.toggle('active');
        content.classList.toggle('show');
    }
}

/**
 * Toggle filtri
 */
function toggleFilters() {
    const filtersBox = document.getElementById('filtersBox');
    if (filtersBox) {
        filtersBox.classList.toggle('active');
    }
}

/**
 * Carica dati CRM da Supabase
 */
async function loadData() {
    try {
        const sb = initSupabase();
        const { data, error } = await sb.from('CRM').select('*');
        
        if (error) throw error;
        
        allData = data || [];
        filteredData = [...allData];
        
        populateFilters();
        updateProgressBars();
        renderActivities();
    } catch (error) {
        console.error('Errore caricamento CRM:', error);
    }
}

/**
 * Popola i filtri con valori unici
 */
function populateFilters() {
    const clienti = [...new Set(allData.map(d => d.cliente_applicato).filter(Boolean))];
    const tipologie = [...new Set(allData.map(d => d.tipologia_formattata).filter(Boolean))];
    const stati = [...new Set(allData.map(d => d.stato).filter(Boolean))];
    
    const filterCliente = document.getElementById('filterCliente');
    const filterTipologia = document.getElementById('filterTipologia');
    const filterStato = document.getElementById('filterStato');
    
    if (filterCliente) {
        filterCliente.innerHTML = '<option value="">Tutti i clienti</option>' + 
            clienti.map(c => `<option value="${c}">${c}</option>`).join('');
    }
    if (filterTipologia) {
        filterTipologia.innerHTML = '<option value="">Tutte le tipologie</option>' + 
            tipologie.map(t => `<option value="${t}">${t}</option>`).join('');
    }
    if (filterStato) {
        filterStato.innerHTML = '<option value="">Tutti gli stati</option>' + 
            stati.map(s => `<option value="${s}">${s}</option>`).join('');
    }
}

/**
 * Applica filtri
 */
function applyFilters() {
    const cliente = document.getElementById('filterCliente')?.value || '';
    const tipologia = document.getElementById('filterTipologia')?.value || '';
    const stato = document.getElementById('filterStato')?.value || '';
    const scadenza = document.getElementById('filterScadenza')?.value || '';
    
    filteredData = allData.filter(item => {
        if (cliente && item.cliente_applicato !== cliente) return false;
        if (tipologia && item.tipologia_formattata !== tipologia) return false;
        if (stato && item.stato !== stato) return false;
        
        if (scadenza) {
            const urgency = getUrgency(item.DataConsegna);
            if (scadenza !== urgency) return false;
        }
        
        return true;
    });
    
    currentPage = 1;
    renderActivities();
    
    // Chiudi filtri
    const filtersBox = document.getElementById('filtersBox');
    if (filtersBox) filtersBox.classList.remove('active');
}

/**
 * Reset filtri
 */
function resetFilters() {
    document.getElementById('filterCliente').value = '';
    document.getElementById('filterTipologia').value = '';
    document.getElementById('filterStato').value = '';
    document.getElementById('filterScadenza').value = '';
    
    filteredData = [...allData];
    currentPage = 1;
    renderActivities();
}

/**
 * Calcola urgenza
 */
function getUrgency(dateStr) {
    if (!dateStr) return 'futuro';
    
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    
    const date = new Date(dateStr);
    date.setHours(0, 0, 0, 0);
    
    if (date < today) return 'scaduto';
    if (date.getTime() === today.getTime()) return 'oggi';
    return 'futuro';
}

/**
 * Aggiorna progress bars
 */
function updateProgressBars() {
    const preventivi = allData.filter(d => d.tipologia_formattata?.toLowerCase().includes('preventiv'));
    const ordini = allData.filter(d => d.tipologia_formattata?.toLowerCase().includes('ordin'));
    
    const prevScaduti = preventivi.filter(d => getUrgency(d.DataConsegna) === 'scaduto').length;
    const ordScaduti = ordini.filter(d => getUrgency(d.DataConsegna) === 'scaduto').length;
    
    const prevPercent = preventivi.length ? Math.round((prevScaduti / preventivi.length) * 100) : 0;
    const ordPercent = ordini.length ? Math.round((ordScaduti / ordini.length) * 100) : 0;
    
    animateProgress('progressPreventivi', 'percentPreventivi', prevPercent);
    animateProgress('progressOrdini', 'percentOrdini', ordPercent);
}

/**
 * Anima progress ring
 */
function animateProgress(ringId, valueId, percent) {
    const ring = document.getElementById(ringId);
    const value = document.getElementById(valueId);
    
    if (ring && value) {
        const circumference = 301.59;
        const offset = circumference - (percent / 100) * circumference;
        
        setTimeout(() => {
            ring.style.strokeDashoffset = offset;
            value.textContent = percent;
        }, 300);
    }
}

/**
 * Renderizza lista attività
 */
function renderActivities() {
    const container = document.getElementById('activitiesList');
    if (!container) return;
    
    const start = (currentPage - 1) * itemsPerPage;
    const end = start + itemsPerPage;
    const pageData = filteredData.slice(start, end);
    
    if (pageData.length === 0) {
        container.innerHTML = '<div class="empty-state">Nessuna attività trovata</div>';
        return;
    }
    
    container.innerHTML = pageData.map(item => {
        const urgency = getUrgency(item.DataConsegna);
        const statusClass = (item.stato || '').toLowerCase().replace(/\s+/g, '-');
        
        return `
            <div class="activity-card">
                <div class="activity-card-header ${urgency}" onclick="toggleActivity(this)">
                    <div class="activity-info">
                        <div class="activity-title">${item.cliente_applicato || 'N/D'}</div>
                        <div class="activity-meta">
                            <div class="meta-item">
                                <span class="meta-label">Tipo:</span> ${item.tipologia_formattata || 'N/D'}
                            </div>
                            <div class="meta-item">
                                <span class="meta-label">Scadenza:</span> ${formatDate(item.DataConsegna)}
                            </div>
                        </div>
                        <div class="activity-footer">
                            <span class="status-badge status-${statusClass}">${item.stato || 'N/D'}</span>
                            <span class="activity-arrow">▼</span>
                        </div>
                    </div>
                </div>
                <div class="activity-details">
                    <div class="details-wrapper">
                        <div class="details-grid">
                            <div class="detail-item">
                                <div class="detail-label">Referente</div>
                                <div class="detail-value">${item.referente || '-'}</div>
                            </div>
                            <div class="detail-item">
                                <div class="detail-label">Note</div>
                                <div class="detail-value">${item.note || '-'}</div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }).join('');
    
    renderPagination();
}

/**
 * Toggle dettagli attività
 */
function toggleActivity(element) {
    const card = element.closest('.activity-card');
    if (card) {
        card.classList.toggle('active');
    }
}

/**
 * Formatta data
 */
function formatDate(dateStr) {
    if (!dateStr) return 'N/D';
    const date = new Date(dateStr);
    return date.toLocaleDateString('it-IT', { day: '2-digit', month: '2-digit', year: 'numeric' });
}

/**
 * Renderizza paginazione
 */
function renderPagination() {
    const container = document.getElementById('pagination');
    if (!container) return;
    
    const totalPages = Math.ceil(filteredData.length / itemsPerPage);
    if (totalPages <= 1) {
        container.innerHTML = '';
        return;
    }
    
    let html = '';
    for (let i = 1; i <= totalPages; i++) {
        html += `<button class="page-btn ${i === currentPage ? 'active' : ''}" onclick="goToPage(${i})">${i}</button>`;
    }
    
    container.innerHTML = html;
}

/**
 * Vai a pagina
 */
function goToPage(page) {
    currentPage = page;
    renderActivities();
}

/* ========================================
   DASHBOARD GANTT
   ======================================== */

const OPERATORS = [
    'Alessandro Colio',
    'Caterina Dello Stritto',
    'Christian D\'Angelo',
    'Federico Cavazza',
    'Marco Garzesi'
];

const TIME_SLOTS = [];
for (let h = 8; h < 18; h++) {
    TIME_SLOTS.push(`${h.toString().padStart(2, '0')}:00`);
    TIME_SLOTS.push(`${h.toString().padStart(2, '0')}:30`);
}

/**
 * Toggle Gantt
 */
function toggleGantt() {
    const header = document.getElementById('ganttHeader');
    const container = document.getElementById('ganttContainer');
    
    if (header && container) {
        header.classList.toggle('active');
        container.classList.toggle('collapsed');
    }
}

/**
 * Renderizza header timeline Gantt
 */
function renderGanttTimelineHeader() {
    const container = document.getElementById('ganttTimelineHeader');
    if (!container) return;
    
    container.innerHTML = TIME_SLOTS.filter((_, i) => i % 2 === 0).map(time => 
        `<div class="gantt-time-slot">${time}</div>`
    ).join('');
}

/**
 * Carica dati Gantt
 */
async function loadGanttData() {
    try {
        const sb = initSupabase();
        const { data, error } = await sb.from('UfficioTecnico').select('*');
        
        if (error) throw error;
        
        const tasksByOperator = groupTasksByOperator(data || []);
        renderGanttSummary(tasksByOperator);
        renderGanttDetails(tasksByOperator);
        updateGanttStats(tasksByOperator);
        
    } catch (error) {
        console.error('Errore caricamento Gantt:', error);
    }
}

/**
 * Raggruppa task per operatore
 */
function groupTasksByOperator(data) {
    const grouped = {};
    
    OPERATORS.forEach(op => {
        grouped[op] = [];
    });
    
    data.forEach(task => {
        OPERATORS.forEach(op => {
            const field2D = task.Disegno_2D;
            const field3D = task.Disegno_3D;
            const fieldDist = task.Distinta;
            
            if (field2D && field2D.includes(op)) {
                grouped[op].push({ ...task, tipo: '2D' });
            }
            if (field3D && field3D.includes(op)) {
                grouped[op].push({ ...task, tipo: '3D' });
            }
            if (fieldDist && fieldDist.includes(op)) {
                grouped[op].push({ ...task, tipo: 'Distinta' });
            }
        });
    });
    
    return grouped;
}

/**
 * Renderizza summary Gantt
 */
function renderGanttSummary(tasksByOperator) {
    const container = document.getElementById('ganttSummaryRows');
    if (!container) return;
    
    container.innerHTML = OPERATORS.map(op => {
        const tasks = tasksByOperator[op] || [];
        const initials = op.split(' ').map(n => n[0]).join('');
        
        return `
            <div class="gantt-summary-row">
                <div class="gantt-person-cell">
                    <div class="person-avatar">${initials}</div>
                </div>
                <div class="gantt-timeline-cell">
                    ${tasks.slice(0, 3).map(t => `
                        <div class="gantt-task-bar mini" style="background: ${getTaskColor(t.tipo)}">
                            ${t.Cliente || 'Task'}
                        </div>
                    `).join('')}
                    ${tasks.length > 3 ? `<span class="more-tasks">+${tasks.length - 3}</span>` : ''}
                </div>
            </div>
        `;
    }).join('');
}

/**
 * Renderizza dettagli Gantt
 */
function renderGanttDetails(tasksByOperator) {
    const container = document.getElementById('ganttDetailsRows');
    if (!container) return;
    
    container.innerHTML = OPERATORS.map(op => {
        const tasks = tasksByOperator[op] || [];
        
        return `
            <div class="gantt-person-section">
                <div class="person-header">
                    <div class="person-avatar">${op.split(' ').map(n => n[0]).join('')}</div>
                    <div class="person-name">${op}</div>
                    <div class="person-tasks-count">${tasks.length} lavori</div>
                </div>
                <div class="person-tasks">
                    ${tasks.map(t => `
                        <div class="task-card">
                            <div class="task-type" style="background: ${getTaskColor(t.tipo)}">${t.tipo}</div>
                            <div class="task-info">
                                <div class="task-title">${t.Cliente || 'N/D'}</div>
                                <div class="task-desc">${t.Commessa || ''}</div>
                            </div>
                        </div>
                    `).join('') || '<div class="no-tasks">Nessun lavoro assegnato</div>'}
                </div>
            </div>
        `;
    }).join('');
}

/**
 * Colore per tipo task
 */
function getTaskColor(tipo) {
    const colors = {
        '2D': '#3b82f6',
        '3D': '#10b981',
        'Distinta': '#f59e0b'
    };
    return colors[tipo] || '#6b7280';
}

/**
 * Aggiorna stats Gantt
 */
function updateGanttStats(tasksByOperator) {
    const totalPersons = document.getElementById('totalPersons');
    const totalTasks = document.getElementById('totalTasks');
    const avgOccupation = document.getElementById('avgOccupation');
    
    let total = 0;
    let occupied = 0;
    
    OPERATORS.forEach(op => {
        const tasks = tasksByOperator[op] || [];
        total += tasks.length;
        if (tasks.length > 0) occupied++;
    });
    
    if (totalPersons) totalPersons.textContent = OPERATORS.length;
    if (totalTasks) totalTasks.textContent = total;
    if (avgOccupation) avgOccupation.textContent = occupied;
}

// Esponi globalmente
window.loadHomeContent = loadHomeContent;
window.toggleDash = toggleDash;
window.toggleFilters = toggleFilters;
window.applyFilters = applyFilters;
window.resetFilters = resetFilters;
window.toggleActivity = toggleActivity;
window.goToPage = goToPage;
window.toggleGantt = toggleGantt;
window.renderGanttTimelineHeader = renderGanttTimelineHeader;
