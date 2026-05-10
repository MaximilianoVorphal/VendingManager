window.modalScrollLock = {
    _count: 0,
    lock: function () {
        this._count++;
        document.body.style.overflow = 'hidden';
    },
    unlock: function () {
        this._count = Math.max(0, this._count - 1);
        if (this._count === 0) document.body.style.overflow = '';
    }
};
