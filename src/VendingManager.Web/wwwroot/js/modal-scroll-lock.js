/**
 * modalScrollLock — iOS-safe scroll lock for modals, bottom sheets, and overlays.
 * -------------------------------------------------------------------------
 * USO DESDE BLAZOR:
 *   await JS.InvokeVoidAsync("modalScrollLock.lock");
 *   // ... modal/sheet visible ...
 *   await JS.InvokeVoidAsync("modalScrollLock.unlock");
 *
 * POR QUÉ NO BASTA CON overflow:hidden:
 *   En iOS Safari, document.body.style.overflow = 'hidden' NO bloquea
 *   el scroll. La página entera se sigue moviendo detrás del modal.
 *   La ÚNICA forma confiable es position: fixed en <html> + preservar
 *   la posición de scroll con top: -scrollY.
 *
 * COMPORTAMIENTO:
 *   - lock():   guarda scrollY, aplica position:fixed con top negativo,
 *               agrega clase .vm-locked a <html> (activa CSS complementaria).
 *   - unlock(): revierte position/top, restaura scrollY, remueve .vm-locked.
 *   - Contador interno: múltiples llamadas a lock() requieren igual
 *     número de unlock() para liberar (soporta sheets anidados).
 *   - Idempotente: llamar unlock() sin lock() previo no rompe nada.
 *   - Resize-safe: si el viewport cambia mientras está locked (rotación,
 *     address bar), la posición de scroll se recalcula al unlockear.
 */
window.modalScrollLock = (function () {
    var _count = 0;
    var _scrollY = 0;
    var _locked = false;

    function lock() {
        _count++;

        if (_locked) return; // ya está locked

        _scrollY = window.scrollY;
        _locked = true;

        // Guardamos el ancho actual para restauración (evita salto horizontal
        // cuando el scrollbar desaparece en desktop, y mantiene el layout en mobile)
        var scrollbarWidth = window.innerWidth - document.documentElement.clientWidth;
        if (scrollbarWidth > 0) {
            document.body.style.paddingRight = scrollbarWidth + 'px';
        }

        document.documentElement.classList.add('vm-locked');
        document.documentElement.style.top = '-' + _scrollY + 'px';
    }

    function unlock() {
        _count = Math.max(0, _count - 1);

        if (_count > 0) return; // todavía hay locks pendientes
        if (!_locked) return;   // ya estaba unlocked

        document.documentElement.classList.remove('vm-locked');
        document.documentElement.style.top = '';
        document.body.style.paddingRight = '';

        _locked = false;

        // Restaurar posición de scroll.
        // Si el viewport cambió mientras estaba locked (ej: rotación),
        // scrollY podría ser inválido. window.scrollTo lo maneja con clamping.
        window.scrollTo(0, _scrollY);
        _scrollY = 0;
    }

    /**
     * Devuelve true si el lock está activo.
     * Útil para debugging desde la consola o para guards condicionales.
     */
    function isLocked() {
        return _locked;
    }

    return {
        lock: lock,
        unlock: unlock,
        isLocked: isLocked
    };
})();
