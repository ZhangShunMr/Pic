window.motorFiles = {
    dirty: false,
    setDirty(value) {
        this.dirty = value;
    },
    download(filename, mime, text) {
        const blob = new Blob([text], { type: mime });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = filename;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        setTimeout(() => URL.revokeObjectURL(url), 30000);
    }
};
window.addEventListener('beforeunload', event => {
    if (window.motorFiles.dirty) {
        event.preventDefault();
        event.returnValue = '';
    }
});
