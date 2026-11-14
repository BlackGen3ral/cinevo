const $ = selector => document.querySelector(selector);
const key = location.hash.slice(1) || sessionStorage.getItem('cinevo-local');
if (location.hash && key) {
  sessionStorage.setItem('cinevo-local', key);
  history.replaceState(null, '', '/');
}
const headers = { Authorization: 'Bearer ' + key };
const state = { section: 'library', search: '', page: 1, movies: [], revision: null, busy: false };
const images = new Map();
const imageObserver = new IntersectionObserver(entries => {
  for (const { target, isIntersecting } of entries) {
    if (!isIntersecting) continue;
    imageObserver.unobserve(target);
    loadImage(target);
  }
}, { rootMargin: '220px' });

function node(tag, className, text) {
  const element = document.createElement(tag);
  if (className) element.className = className;
  if (text) element.textContent = text;
  return element;
}

function button(text, style, run) {
  const element = node('button', style ? 'button ' + style : '', text);
  element.type = 'button';
  element.onclick = run;
  return element;
}

function command(text, style, name, id) {
  return button(text, style, event => send(name, id, event.currentTarget));
}

async function request(path, options = {}) {
  const response = await fetch(path, { ...options, headers: { ...headers, ...options.headers } });
  if (!response.ok) {
    const info = await response.json().catch(() => ({}));
    throw new Error(info.error || 'Abra a interface pelo aplicativo Cinevo.');
  }
  return response;
}

async function send(name, id, trigger) {
  if (trigger) trigger.disabled = true;
  try {
    await request('/api/action', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name, id })
    });
    if (['favorite', 'watched', 'add-to-library'].includes(name)) {
      if (name === 'add-to-library' && $('#details').open) $('#details').close();
      await refresh(false, true);
    }
  } catch (error) {
    $('#status').textContent = error.message;
  } finally {
    if (trigger && trigger.isConnected) trigger.disabled = false;
  }
}

function poster(movie, className) {
  if (!movie.hasCover) return null;
  const image = node('img', className);
  image.alt = '';
  image.decoding = 'async';
  image.dataset.cover = movie.id + '?v=' + movie.coverVersion;
  return image;
}

async function loadImage(image) {
  const identity = image.dataset.cover;
  try {
    if (!images.has(identity)) {
      const pending = request('/api/cover/' + identity).then(response => response.blob());
      images.set(identity, pending);
      if (images.size > 90) images.delete(images.keys().next().value);
    }
    const data = await images.get(identity);
    if (!image.isConnected || image.src) return;
    const url = URL.createObjectURL(data);
    image.onload = image.onerror = () => URL.revokeObjectURL(url);
    image.src = url;
  } catch {
    images.delete(identity);
    image.remove();
  }
}

function playActions(movie) {
  const actions = node('div', 'actions');
  actions.append(command(movie.playLabel, 'primary', 'play', movie.id));
  return actions;
}

function renderHero(movie, home) {
  const target = $('#hero');
  target.hidden = !home;
  if (!home) return;
  target.classList.toggle('hero-empty', !movie?.hasCover);
  const shade = node('div', 'hero-shade');
  const content = node('div', 'hero-content');
  target.replaceChildren(shade, content);
  if (movie) {
    const art = poster(movie, 'hero-art');
    if (art) { target.prepend(art); imageObserver.observe(art); }
    content.append(node('p', 'eyebrow', movie.progress > 0 && !movie.watched ? 'Continue assistindo' : 'Em destaque'),
      node('h1', '', movie.title));
    const metadata = [movie.year, movie.genre, movie.isOnline ? 'Coleção online' : 'Sua biblioteca'].filter(Boolean).join(' · ');
    content.append(node('p', 'meta', metadata));
    if (movie.description) content.append(node('p', 'description', movie.description));
    const actions = playActions(movie);
    actions.append(button('Mais informações', 'secondary', () => showDetails(movie)));
    content.append(actions);
  } else {
    content.append(node('h1', '', 'Seu cinema, em casa.'),
      node('p', 'description', 'Adicione uma pasta do seu PC ou importe títulos de um catálogo online.'));
    const actions = node('div', 'actions');
    actions.append(command('Adicionar pasta', 'primary', 'add-folder'),
      button('Importar endereço', 'secondary', () => { changeSection('imports'); openImport(); }));
    content.append(actions);
  }
}

function movieCard(movie) {
  const card = node('article', 'card');
  card.dataset.movie = movie.id;
  const open = node('button', 'poster-button');
  open.title = movie.title;
  open.onclick = () => showDetails(movie);
  const art = node('span', 'poster', movie.title[0]?.toUpperCase() || 'C');
  const image = poster(movie);
  if (image) { art.append(image); imageObserver.observe(image); }
  if (state.section === 'imports' && !movie.importedOnly) art.append(node('span', 'label', 'Na biblioteca'));
  if (!movie.watched && movie.progress > 0) {
    const progress = node('progress');
    progress.max = 100;
    progress.value = movie.progress;
    progress.setAttribute('aria-label', 'Progresso assistido');
    art.append(progress);
  }
  open.append(art, node('strong', '', movie.title), node('small', '', [movie.year, movie.genre].filter(Boolean).join(' · ')));
  card.append(open);
  if (movie.importedOnly) {
    const add = command('+ Adicionar à biblioteca', 'secondary', 'add-to-library', movie.id);
    add.setAttribute('aria-label', 'Adicionar à biblioteca: ' + movie.title);
    card.append(add);
  }
  return card;
}

function shelf(title, movies, rail = false) {
  const section = node('section', 'shelf');
  const cards = node('div', rail ? 'rail' : 'grid');
  cards.append(...movies.map(movieCard));
  if (rail) {
    const controls = node('div', 'shelf-controls');
    for (const [direction, label] of [[-1, 'Voltar na fileira'], [1, 'Avançar na fileira']]) {
      const arrow = button(direction < 0 ? '‹' : '›', '', () => cards.scrollBy({
        left: direction * cards.clientWidth * .85,
        behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth'
      }));
      arrow.setAttribute('aria-label', label);
      controls.append(arrow);
    }
    section.append(controls);
  }
  if (title) section.append(node('h2', '', title));
  section.append(cards);
  return section;
}

function render(data) {
  imageObserver.disconnect();
  const home = state.section === 'library' && !state.search;
  renderHero(data.spotlight, home);
  const heading = $('#page-head');
  heading.hidden = home;
  heading.replaceChildren();
  if (!home) {
    const info = node('div');
    const title = state.section === 'imports' ? 'Filmes importados' : state.section === 'favorites' ? 'Favoritos' : 'Minha biblioteca';
    info.append(node('h1', '', title));
    if (state.section === 'imports') {
      info.append(node('p', '', 'Escolha quais títulos você quer guardar na sua biblioteca.'));
      const actions = node('div', 'actions');
      actions.append(button('+ Importar endereço', 'accent', openImport), command('Sincronizar portal', 'secondary', 'sync'));
      heading.append(info, actions);
    } else {
      heading.append(info);
    }
  }
  const rows = $('#rows');
  rows.replaceChildren();
  if (home && data.returning?.length) rows.append(shelf('Continue assistindo', data.returning, true));
  if (home && data.favorites?.length) rows.append(shelf('Seus favoritos', data.favorites, true));
  if (home && state.movies.length > 1) rows.append(shelf('Adicionados recentemente', state.movies.slice(0, 12), true));
  if (state.movies.length) {
    rows.append(shelf(home ? 'Minha biblioteca' : '', state.movies));
  } else if (!home || state.search) {
    const text = state.search ? 'Nenhum título corresponde à sua busca.' :
      state.section === 'imports' ? 'Cole o endereço de um filme ou catálogo para começar.' : 'Você ainda não marcou nenhum filme como favorito.';
    rows.append(node('p', 'empty', text));
  }
  $('#more').hidden = !data.more;
  $('#imports-count').hidden = !data.imported;
  $('#imports-count').textContent = data.imported;
  $('#version').textContent = 'Cinevo ' + data.version + ' · Beta';
}

function showDetails(movie) {
  detailId = movie.id;
  const panel = $('#detail-content');
  const title = node('h2', '', movie.title);
  title.id = 'detail-title';
  panel.replaceChildren(title);
  const tags = [movie.year, movie.genre, movie.watched ? 'Assistido' : '', movie.rating ? '★ ' + movie.rating + '/5' : ''].filter(Boolean);
  if (tags.length) panel.append(node('p', 'meta', tags.join(' · ')));
  if (movie.description) panel.append(node('p', 'description', movie.description));
  const actions = playActions(movie);
  if (movie.importedOnly) actions.append(command('+ Adicionar à biblioteca', 'accent', 'add-to-library', movie.id));
  actions.append(command(movie.favorite ? 'Remover favorito' : 'Favoritar', 'secondary', 'favorite', movie.id),
    command(movie.watched ? 'Marcar como não assistido' : 'Marcar como assistido', 'secondary', 'watched', movie.id),
    command(movie.isOnline ? 'Editar e ver downloads' : 'Editar detalhes', 'secondary', 'details', movie.id));
  panel.append(actions);
  if (!$('#details').open) $('#details').showModal();
}

async function catalog(page, limit, signal) {
  const query = new URLSearchParams({ section: state.section, search: state.search, page, limit });
  return (await request('/api/catalog?' + query, { signal })).json();
}

let fetchCatalog;
let firstPage;
async function refresh(append = false, keepPages = false) {
  fetchCatalog?.abort();
  const current = fetchCatalog = new AbortController();
  const requestedPage = append ? state.page + 1 : 1;
  const pages = keepPages ? state.page : 1;
  const focused = document.activeElement.closest('[data-movie]')?.dataset.movie;
  state.busy = true;
  try {
    const results = await Promise.all(Array.from({ length: pages }, (_, index) =>
      catalog(requestedPage + index, 30, current.signal)));
    const data = results[0];
    const movies = results.flatMap(result => result.movies);
    data.more = results.at(-1).more;
    if (current.signal.aborted) return;
    state.page = requestedPage + pages - 1;
    state.movies = append ? state.movies.concat(movies) : movies;
    if (!append) firstPage = data;
    render(append ? { ...firstPage, ...data, spotlight: firstPage.spotlight, returning: firstPage.returning, favorites: firstPage.favorites } : data);
    if (focused) $('[data-movie="' + focused + '"] button')?.focus({ preventScroll: true });
    if ($('#details').open && detailId) {
      const updated = state.movies.find(movie => movie.id === detailId);
      if (updated) showDetails(updated);
    }
    $('#main').setAttribute('aria-busy', 'false');
  } catch (error) {
    if (!current.signal.aborted) $('#status').textContent = error.message;
  } finally {
    if (fetchCatalog === current) state.busy = false;
  }
}

function changeSection(section) {
  state.section = section;
  state.search = '';
  $('#search').value = '';
  for (const tab of document.querySelectorAll('nav button')) {
    if (tab.dataset.section === section) tab.setAttribute('aria-current', 'page');
    else tab.removeAttribute('aria-current');
  }
  window.scrollTo(0, 0);
  refresh();
}

const importing = { next: '', continuation: false, running: false, paused: false, seen: new Set() };
function openImport() {
  if (!$('#import-dialog').open) $('#import-dialog').showModal();
}

async function runImport() {
  if (!importing.next || importing.running) return;
  importing.running = true;
  importing.paused = false;
  $('#start-import').disabled = $('#import-url').disabled = $('#close-import').disabled = true;
  $('#pause-import').hidden = $('#pause-import').disabled = false;
  $('#resume-import').hidden = true;
  const progress = $('#import-status');
  let count = 0;
  let message = '';
  try {
    while (importing.next && !importing.paused) {
      const address = importing.next;
      progress.textContent = count ? count + ' títulos processados. Buscando próximas páginas…' : 'Buscando filmes no endereço…';
      const result = await (await request('/api/action', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: 'import-batch', address, continuation: importing.continuation })
      })).json();
      count += result.count;
      importing.next = result.next || '';
      importing.continuation = true;
      if (result.partial) { message = result.partial; break; }
      importing.seen.add(address);
      if (importing.seen.has(importing.next)) {
        message = 'O site repetiu uma página do catálogo.';
        importing.next = '';
        break;
      }
      message = result.message;
    }
    if (importing.paused && importing.next) message = 'Importação pausada.';
  } catch (error) {
    message = error.message;
    if (!importing.continuation) importing.next = '';
  } finally {
    $('#pause-import').hidden = true;
    if (count) {
      progress.textContent = 'Atualizando Filmes importados…';
      try {
        await request('/api/action', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ name: 'sync-imports' })
        });
        await refresh();
      } catch (error) { message = error.message; }
    }
    if (count > 1) {
      const summary = count + ' títulos processados.';
      message = message.endsWith('processados.') ? summary : summary + ' ' + message;
    }
    progress.textContent = message;
    importing.running = false;
    $('#resume-import').hidden = !importing.next;
    $('#start-import').disabled = $('#import-url').disabled = $('#close-import').disabled = false;
  }
}
$('#import-form').onsubmit = event => {
  event.preventDefault();
  if (importing.running) return;
  try {
    const address = new URL($('#import-url').value);
    if (address.protocol !== 'https:') throw new Error();
    address.hash = '';
    importing.next = address.href;
  } catch {
    $('#import-status').textContent = 'Cole um endereço HTTPS público completo.';
    return;
  }
  importing.continuation = false;
  importing.seen.clear();
  runImport();
};
$('#pause-import').onclick = () => {
  importing.paused = true;
  $('#pause-import').disabled = true;
  $('#import-status').textContent = 'Pausando após as páginas em andamento…';
};
$('#resume-import').onclick = runImport;
$('#close-import').onclick = () => $('#import-dialog').close();
$('#import-dialog').addEventListener('cancel', event => {
  if (importing.running) event.preventDefault();
});

let detailId;
document.addEventListener('click', event => {
  const section = event.target.closest('[data-section]');
  if (section) changeSection(section.dataset.section);
  const action = event.target.closest('[data-command]');
  if (action) {
    $('details.menu').open = false;
    send(action.dataset.command, null, action);
  }
});
$('.dialog-close').onclick = () => $('#details').close();
$('#details').addEventListener('close', () => { detailId = null; });
$('#more').onclick = () => { if (!state.busy) refresh(true); };
let searchTimer;
$('#search').oninput = event => {
  state.search = event.target.value.trim();
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => refresh(), 150);
};

async function checkRevision(initial = false) {
  if (state.busy || (!initial && document.hidden)) return;
  try {
    const data = await (await request('/api/status')).json();
    if (state.busy) return;
    $('#status').textContent = data.status;
    $('#update').hidden = !data.updating;
    $('#scan').textContent = data.scanning ? 'Cancelar busca de arquivos' : 'Atualizar arquivos';
    $('#scan').dataset.command = data.scanning ? 'cancel-scan' : 'scan';
    if (state.revision === data.revision) return;
    state.revision = data.revision;
    await refresh(false, true);
  } catch { /* A janela pode estar fechando. */ }
}
if (/^[0-9A-F]{64}$/.test(key || '')) {
  checkRevision(true);
  setInterval(checkRevision, 1800);
} else {
  $('#status').textContent = 'Abra a interface pelo aplicativo Cinevo.';
}
