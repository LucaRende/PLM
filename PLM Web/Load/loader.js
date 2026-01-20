/* =============================================
   PLM Web - Loader JS
   Utilizzo:
   - Loader.show('pagina', 'Caricamento...')
   - Loader.show('funzione', 'Elaborazione...')
   - Loader.show('invio', 'Salvataggio...')
   - Loader.inline(elemento, 'Caricamento...')
   - Loader.hide()
   - Loader.hideInline(elemento)
   ============================================= */

const Loader = {
    overlay: null,
    
    // Genera HTML in base al tipo
    getHTML(tipo, testo) {
        switch(tipo) {
            case 'pagina':
                return `
                    <div class="loader-pagina">
                        <div class="spinner"></div>
                        ${testo ? `<div class="testo">${testo}</div>` : ''}
                    </div>
                `;
            
            case 'funzione':
                return `
                    <div class="loader-funzione">
                        <div class="dots">
                            <div class="dot"></div>
                            <div class="dot"></div>
                            <div class="dot"></div>
                        </div>
                        ${testo ? `<div class="testo">${testo}</div>` : ''}
                    </div>
                `;
            
            case 'invio':
                return `
                    <div class="loader-invio">
                        <div class="bar-container">
                            <div class="bar"></div>
                        </div>
                        ${testo ? `<div class="testo">${testo}</div>` : ''}
                    </div>
                `;
            
            default:
                return `
                    <div class="loader-pagina">
                        <div class="spinner"></div>
                        ${testo ? `<div class="testo">${testo}</div>` : ''}
                    </div>
                `;
        }
    },
    
    // Mostra loader overlay
    show(tipo = 'pagina', testo = '') {
        // Rimuovi eventuale loader esistente
        this.hide();
        
        // Crea overlay
        this.overlay = document.createElement('div');
        this.overlay.className = `loader-overlay tipo-${tipo}`;
        this.overlay.innerHTML = this.getHTML(tipo, testo);
        
        // Aggiungi al body
        document.body.appendChild(this.overlay);
        
        // Blocca scroll
        document.body.style.overflow = 'hidden';
        
        // Per tipo "pagina" attiva subito, per altri usa animazione
        if (tipo === 'pagina') {
            this.overlay.classList.add('active');
        } else {
            requestAnimationFrame(() => {
                this.overlay.classList.add('active');
            });
        }
    },
    
    // Nasconde loader overlay
    hide() {
        if (this.overlay) {
            // Per tipo pagina usa classe hiding per fade out
            if (this.overlay.classList.contains('tipo-pagina')) {
                this.overlay.classList.add('hiding');
            } else {
                this.overlay.classList.remove('active');
            }
            
            // Rimuovi dopo animazione
            setTimeout(() => {
                if (this.overlay && this.overlay.parentNode) {
                    this.overlay.parentNode.removeChild(this.overlay);
                }
                this.overlay = null;
            }, 300);
            
            // Ripristina scroll
            document.body.style.overflow = '';
        }
    },
    
    // Loader inline (dentro un elemento)
    inline(elemento, testo = 'Caricamento...') {
        if (typeof elemento === 'string') {
            elemento = document.querySelector(elemento);
        }
        
        if (!elemento) return;
        
        // Salva contenuto originale
        elemento.dataset.originalContent = elemento.innerHTML;
        elemento.dataset.loaderActive = 'true';
        
        // Inserisci loader inline
        elemento.innerHTML = `
            <div class="loader-inline">
                <div class="mini-spinner"></div>
                <span class="testo">${testo}</span>
            </div>
        `;
        
        // Disabilita se è un bottone
        if (elemento.tagName === 'BUTTON') {
            elemento.disabled = true;
        }
    },
    
    // Rimuovi loader inline
    hideInline(elemento) {
        if (typeof elemento === 'string') {
            elemento = document.querySelector(elemento);
        }
        
        if (!elemento || elemento.dataset.loaderActive !== 'true') return;
        
        // Ripristina contenuto originale
        elemento.innerHTML = elemento.dataset.originalContent || '';
        elemento.dataset.loaderActive = 'false';
        
        // Riabilita se è un bottone
        if (elemento.tagName === 'BUTTON') {
            elemento.disabled = false;
        }
    },
    
    // Aggiorna testo del loader attivo
    updateText(nuovoTesto) {
        if (this.overlay) {
            const testoEl = this.overlay.querySelector('.testo');
            if (testoEl) {
                testoEl.textContent = nuovoTesto;
            }
        }
    }
};

// Esporta per moduli (se necessario)
if (typeof module !== 'undefined' && module.exports) {
    module.exports = Loader;
}
