window.fullvantage = window.fullvantage || {};

window.fullvantage.downloadZip = async function(url, payload) {
  try {
    const resp = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
    if (!resp.ok) {
      const text = await resp.text();
      alert('Build failed: ' + text);
      return;
    }
    const blob = await resp.blob();
    const link = document.createElement('a');
    const href = URL.createObjectURL(blob);
    link.href = href;
    link.download = 'FullVantage.Agent.zip';
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(href);
  } catch (e) {
    console.error(e);
    alert('Build failed: ' + e);
  }
};
