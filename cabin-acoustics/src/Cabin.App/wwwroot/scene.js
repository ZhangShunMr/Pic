window.cabinSvgPoint = (svg, clientX, clientY) => {
    const point = new DOMPoint(clientX, clientY);
    const matrix = svg.getScreenCTM();
    if (!matrix) return [-1, -1];
    const local = point.matrixTransform(matrix.inverse());
    return [local.x, local.y];
};
window.cabinDirty = false;
window.cabinGuard = value => { window.cabinDirty = value; };
window.addEventListener('beforeunload', event => {
    if (window.cabinDirty) {
        event.preventDefault();
        event.returnValue = '';
    }
});
