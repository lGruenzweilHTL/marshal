// German texts for German browsers, and a retry that reloads the page the user wanted.
if ((navigator.language || '').toLowerCase().startsWith('de')) {
  document.documentElement.lang = 'de'
  for (const element of document.querySelectorAll('[data-de]')) element.textContent = element.getAttribute('data-de')
  document.title = 'Marshal ist nicht erreichbar'
}
document.getElementById('retry').addEventListener('click', () => location.reload())
