window.descargarArchivo = (content, fileName) => {
    try {
        let byteArray;

        // Robust input handling: check if content is string (Base64), Array, or Uint8Array
        if (typeof content === 'string') {
            // Base64 string case
            const byteCharacters = atob(content);
            const byteNumbers = new Array(byteCharacters.length);
            for (let i = 0; i < byteCharacters.length; i++) {
                byteNumbers[i] = byteCharacters.charCodeAt(i);
            }
            byteArray = new Uint8Array(byteNumbers);
        } else if (content instanceof Uint8Array) {
            // Already a Uint8Array
            byteArray = content;
        } else if (Array.isArray(content)) {
            // Standard JS Array of numbers
            byteArray = new Uint8Array(content);
        } else {
            // Fallback/Error
            console.error("descargarArchivo: Tipo de contenido desconocido", typeof content);
            return;
        }

        // Create blob
        const blob = new Blob([byteArray], { type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" });

        // Create download link
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName || "reporte.xlsx";

        // Trigger download
        document.body.appendChild(anchor);
        anchor.click();

        // Cleanup
        document.body.removeChild(anchor);
        URL.revokeObjectURL(url);
    } catch (error) {
        console.error("Error en descargarArchivo:", error);
    }
};
