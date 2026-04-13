console.log('ChartJsInterop loading...');
window.ChartJsInterop = {
    setupChart: (canvasId, config) => {
        const ctx = document.getElementById(canvasId).getContext('2d');
        if (window[canvasId + '_chart']) {
            window[canvasId + '_chart'].destroy();
        }
        window[canvasId + '_chart'] = new Chart(ctx, config);
    }
};
