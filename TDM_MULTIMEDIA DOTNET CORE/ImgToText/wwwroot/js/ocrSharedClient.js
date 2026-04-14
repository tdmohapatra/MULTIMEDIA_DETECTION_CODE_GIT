window.OcrSharedClient = (() => {
    const imageOnlyAccept = "image/*";

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    return {
        imageOnlyAccept,
        escapeHtml
    };
})();
