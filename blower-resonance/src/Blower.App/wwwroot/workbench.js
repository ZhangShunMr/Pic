window.blower = {
    dirty: false,
    setDirty(value) {
        this.dirty = value;
    }
};
window.addEventListener('beforeunload', event => {
    if (window.blower.dirty) {
        event.preventDefault();
        event.returnValue = '';
    }
});
