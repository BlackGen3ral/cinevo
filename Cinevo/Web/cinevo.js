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
    if (art) {
      target.prepend(art);
      loadImage(art);
      art.addEventListener('load', () => {
        if (art.naturalWidth < art.naturalHeight || art.naturalWidth < 1000) {
          art.classList.add('hero-poster');
          art.style.maxWidth = art.naturalWidth + 'px';
          art.style.maxHeight = art.naturalHeight + 'px';
        }
      }, { once: true });
    }
    content.append(node('p', 'eyebrow', movie.progress > 0 && !movie.watched ? 'Continue assistindo' : 'Em destaque'),
      node('h1', '', movie.title));
    const metadata = [movie.year, movie.genre, movie.isOnline ? 'Coleção online' : 'Sua biblioteca'].filter(Boolean).join(' · ');
    content.append(node('p', 'meta', metadata));
    if (movie.description) content.append(node('p', 'description', movie.description));
    const actions = playActions(movie);
    actions.append(button('Mais informações', 'secondary', () => showDetails(movie)));
    content.append(actions);
    if (heroTotal > 1) {
      const controls = node('div', 'hero-controls');
      const previous = button('‹', 'secondary', () => stepHero(-1));
      const next = button('›', 'secondary', () => stepHero(1));
      previous.setAttribute('aria-label', 'Destaque anterior');
      next.setAttribute('aria-label', 'Próximo destaque');
      const pause = button(heroPaused ? 'Retomar' : 'Pausar', 'secondary', () => {
        heroPaused = !heroPaused;
        pause.textContent = heroPaused ? 'Retomar' : 'Pausar';
      });
      controls.append(previous, node('span', '', (heroIndex + 1) + ' / ' + heroTotal), next, pause);
      content.append(controls);
    }
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

function shelf(title, rail = false) {
  const section = node('section', 'shelf');
  const cards = node('div', rail ? 'rail' : 'grid');
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

// Reuse the nodes already on screen, including decoded images and scroll positions.
const shelves = new Map();
let heroKey, headingKey, detailKey;
let heroIndex = 0, heroTotal = 0, heroSlide, heroLoading = false;
let heroPaused = matchMedia('(prefers-reduced-motion: reduce)').matches;
async function stepHero(direction) {
  if (heroLoading || heroTotal < 2 || $('#hero').hidden) return;
  heroLoading = true;
  const index = (heroIndex + direction + heroTotal) % heroTotal;
  try {
    const movie = await transferAction('hero-slide', null, String(index));
    if (movie && !$('#hero').hidden) {
      heroSlide = movie;
      heroIndex = index;
      // Keep the current picture visible while loading the next one.
      if (movie.hasCover) {
        const cover = movie.id + '?v=' + movie.coverVersion;
        if (!images.has(cover)) images.set(cover, request('/api/cover/' + cover).then(r => r.blob()));
        await images.get(cover).catch(() => images.delete(cover));
        if (images.size > 90) images.delete(images.keys().next().value);
      }
      if (!$('#hero').hidden) {
        renderHero(movie, true);
        heroKey = JSON.stringify([true, movie, heroTotal]);
      }
    }
  } catch { /* Preserve the current highlight if the local service is busy. */ }
  finally { heroLoading = false; }
}
setInterval(() => {
  const hero = $('#hero');
  if (!heroPaused && !document.hidden && !document.querySelector('dialog[open]') &&
      !hero.matches(':hover, :focus-within') && hero.getBoundingClientRect().bottom > 120) stepHero(1);
}, 7200);
function renderShelf(id, title, movies, rail, index) {
  let section = shelves.get(id);
  if (!section) {
    section = shelf(title, rail);
    shelves.set(id, section);
  }
  const rows = $('#rows');
  if (rows.children[index] !== section) rows.insertBefore(section, rows.children[index] || null);
  const cards = section.lastElementChild;
  const previous = new Map(Array.from(cards.children, card => [card.dataset.movie, card]));
  movies.forEach((movie, position) => {
    const key = JSON.stringify(movie);
    const old = previous.get(movie.id);
    let card = old;
    if (old?.dataset.signature !== key) {
      card = movieCard(movie);
      card.dataset.signature = key;
      const oldImage = old?.querySelector('img');
      const newImage = card.querySelector('img');
      if (oldImage?.src && oldImage.dataset.cover === newImage?.dataset.cover) {
        imageObserver.unobserve(newImage);
        newImage.replaceWith(oldImage);
      }
      old?.replaceWith(card);
    }
    if (cards.children[position] !== card) cards.insertBefore(card, cards.children[position] || null);
    previous.delete(movie.id);
  });
  for (const old of previous.values()) {
    const image = old.querySelector('img');
    if (image) imageObserver.unobserve(image);
    old.remove();
  }
}

function render(data) {
  const home = state.section === 'library' && !state.search;
  if (heroTotal !== data.total || !home) { heroIndex = 0; heroSlide = null; }
  heroTotal = data.total;
  if (heroIndex === 0) heroSlide = null;
  const spotlight = heroSlide ? state.movies.find(movie => movie.id === heroSlide.id) || heroSlide : data.spotlight;
  const nextHero = JSON.stringify([home, spotlight, heroTotal]);
  if (nextHero !== heroKey) {
    const oldImage = $('#hero .hero-art');
    if (oldImage) imageObserver.unobserve(oldImage);
    renderHero(spotlight, home);
    const newImage = $('#hero .hero-art');
    if (home && oldImage?.src && oldImage.dataset.cover === newImage?.dataset.cover) {
      imageObserver.unobserve(newImage);
      newImage.replaceWith(oldImage);
    }
    heroKey = nextHero;
  }
  const heading = $('#page-head');
  heading.hidden = home;
  if (!home && headingKey !== state.section) {
    headingKey = state.section;
    heading.replaceChildren();
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
  const visibleShelves = new Set();
  function showShelf(id, title, movies, rail = false) {
    renderShelf(id, title, movies, rail, visibleShelves.size);
    visibleShelves.add(id);
  }
  if (home && data.returning?.length) showShelf('returning', 'Continue assistindo', data.returning, true);
  if (home && data.favorites?.length) showShelf('favorites', 'Seus favoritos', data.favorites, true);
  if (home && state.movies.length > 1) showShelf('recent', 'Adicionados recentemente', state.movies.slice(0, 12), true);
  if (state.movies.length) {
    if (state.section === 'imports') {
      const bySite = Map.groupBy(state.movies, movie => movie.site || 'Origem desconhecida');
      for (const [site, movies] of bySite) showShelf('site-' + site, site, movies);
    } else {
      showShelf(state.section + (home ? '-home' : '-grid'), home ? 'Minha biblioteca' : '', state.movies);
    }
    rows.querySelector('.empty')?.remove();
  } else if (!home || state.search) {
    const text = state.search ? 'Nenhum título corresponde à sua busca.' :
      state.section === 'imports' ? 'Cole o endereço de um filme ou catálogo para começar.' : 'Você ainda não marcou nenhum filme como favorito.';
    let empty = rows.querySelector('.empty');
    if (!empty) { empty = node('p', 'empty'); rows.append(empty); }
    empty.textContent = text;
  } else {
    rows.querySelector('.empty')?.remove();
  }
  for (const [id, section] of shelves) {
    if (visibleShelves.has(id)) continue;
    for (const image of section.querySelectorAll('img')) imageObserver.unobserve(image);
    section.remove();
    shelves.delete(id);
  }
  $('#more').hidden = !data.more;
  $('#imports-count').hidden = !data.imported;
  $('#imports-count').textContent = data.imported;
  $('#version').textContent = 'Cinevo ' + data.version + ' · Beta';
  $('#clear-library').disabled = !data.total && !data.folderCount;
}

const movieDetails = new Map();
function showDetails(movie) {
  const signature = JSON.stringify(movie);
  if ($('#details').open && detailKey === signature) return;
  const dialog = $('#details');
  const isSameMovie = dialog.open && detailId === movie.id;
  detailKey = signature;
  detailId = movie.id;
  const panel = $('#detail-content');
  const title = node('h2', '', movie.title);
  title.id = 'detail-title';
  const introduction = node('div', 'detail-intro');
  introduction.append(title);
  const tags = [movie.year, movie.genre, movie.watched ? 'Assistido' : '', movie.rating ? '★ ' + movie.rating + '/5' : ''].filter(Boolean);
  if (tags.length) introduction.append(node('p', 'detail-tags', tags.join('  ·  ')));
  const mainActions = playActions(movie);
  if (movie.importedOnly) mainActions.append(command('+ Adicionar à biblioteca', 'accent', 'add-to-library', movie.id));
  introduction.append(mainActions);
  const artwork = node('div', 'detail-artwork');
  artwork.append(node('span', '', movie.title[0]?.toUpperCase() || 'C'));
  const image = poster(movie, 'detail-poster');
  if (image) {
    image.alt = 'Capa de ' + movie.title;
    artwork.append(image);
  }
  const header = node('div', 'detail-header');
  header.append(artwork, introduction);
  const information = node('div', 'detail-information');
  const synopsis = node('section', 'detail-synopsis');
  synopsis.append(node('h3', '', 'Sinopse'));
  const summary = node('p', '', movie.description || 'Buscando sinopse…');
  synopsis.append(summary);
  const tools = node('div', 'actions detail-tools');
  tools.append(command(movie.favorite ? 'Remover favorito' : 'Favoritar', 'secondary', 'favorite', movie.id),
    command(movie.watched ? 'Marcar como não assistido' : 'Marcar como assistido', 'secondary', 'watched', movie.id),
    command('Editar detalhes', 'secondary', 'details', movie.id));
  if (movie.isOnline) tools.append(button('Opções de download', 'secondary', event => showSourceDownloads(movie, event.currentTarget)));
  if (!movie.importedOnly) tools.append(button('Remover da biblioteca', 'secondary', () => {
    $('#details').close();
    confirmRemoval('Remover da biblioteca?', movie.title + (movie.isOnline
      ? ' volta para Filmes importados. Os arquivos do PC são preservados.'
      : ' sai da biblioteca, e o arquivo continua no PC.'), async () => {
      await transferAction('remove-movie', movie.id);
      await refresh(false, true);
    });
  }));
  const trailer = isSameMovie && panel.querySelector('.detail-trailer') || node('section', 'detail-trailer');
  const credits = node('section', 'tmdb-credits');
  if (movie.tmdbConfigured === false) renderTmdbStatus(credits, movie, 'disconnected');
  const downloads = isSameMovie && panel.querySelector('.source-downloads');
  information.append(synopsis, trailer, credits, tools);
  if (downloads) information.append(downloads);
  panel.replaceChildren(header, information);
  if (!dialog.open) {
    dialog.showModal();
    dialog.scrollTop = 0;
  }
  if (image) loadImage(image);

  const key = JSON.stringify([movie.id, movie.title, movie.year, movie.description, movie.customTrailer, movie.tmdbRevision, movie.tmdbLink, movie.tmdbConfigured]);
  if (!movieDetails.has(key)) {
    movieDetails.set(key, transferAction('movie-details', movie.id).catch(() => ({ retry: true })).then(details => {
      if (details?.retry) movieDetails.delete(key);
      return details;
    }));
    if (movieDetails.size > 150) movieDetails.delete(movieDetails.keys().next().value);
  }
  movieDetails.get(key).then(details => {
    if (!summary.isConnected || !dialog.open) return;
    summary.textContent = details?.synopsis || movie.description || 'Sinopse indisponível para este título.';
    if (details?.synopsisSource) {
      const citation = node('a', 'detail-source', 'Fonte da sinopse · Wikipédia');
      citation.href = details.synopsisSource;
      citation.target = '_blank';
      citation.rel = 'noopener noreferrer';
      synopsis.append(citation);
    }
    if (details?.tmdb) renderTmdb(credits, introduction, synopsis, details.tmdb, details.tmdbSynopsis, movie);
    else if (details?.tmdbState && details.tmdbState !== 'disconnected')
      renderTmdbStatus(credits, movie, details.tmdbState, details.tmdbError);
    renderMovieTrailer(trailer, mainActions, movie, details?.trailerId);
  });
}

function renderTmdbStatus(section, movie, status, error) {
  const messages = {
    disconnected: 'Conecte o TMDB para carregar a sinopse, a nota, o elenco e os trailers cadastrados.',
    unmatched: 'O TMDB não encontrou uma correspondência para este título. Você pode vincular a ficha pelo endereço.',
    error: error || 'O TMDB não conseguiu carregar esta ficha.'
  };
  const actions = node('div', 'actions');
  if (status === 'unmatched') {
    const search = node('a', 'detail-source', 'Buscar título no TMDB ↗');
    search.href = 'https://www.themoviedb.org/search?language=pt-BR&query=' + encodeURIComponent(movie.title.replace(/\s*\(\d{4}\)\s*$/, ''));
    search.target = '_blank';
    search.rel = 'noopener noreferrer';
    actions.append(search, command('Vincular ficha em Editar detalhes', 'secondary', 'details', movie.id));
  } else {
    actions.append(command('Conectar TMDB', 'secondary', 'tmdb-settings'));
  }
  section.replaceChildren(node('h3', '', 'The Movie Database'), node('p', '', messages[status]), actions);
}

function renderTmdb(section, introduction, synopsis, tmdb, usesSynopsis, movie) {
  const link = node('a', 'detail-source', 'Ver ficha no TMDB ↗');
  link.href = tmdb.url;
  link.target = '_blank';
  link.rel = 'noopener noreferrer';
  const attribution = node('div', 'tmdb-attribution');
  const logo = node('img');
  logo.src = '/tmdb.svg';
  logo.alt = 'The Movie Database';
  const home = node('a');
  home.href = 'https://www.themoviedb.org/?language=pt-BR';
  home.target = '_blank';
  home.rel = 'noopener noreferrer';
  home.append(logo);
  attribution.append(home, link);
  if (tmdb.votes > 0) {
    const score = node('p', 'tmdb-score');
    score.append(node('strong', '', '★ ' + tmdb.score.toLocaleString('pt-BR', { maximumFractionDigits: 1 }) + ' / 10'),
      node('span', '', 'TMDB · ' + tmdb.votes.toLocaleString('pt-BR') + ' avaliações'));
    introduction.querySelector('.actions').before(score);
  }
  if (usesSynopsis) {
    const source = link.cloneNode(true);
    source.textContent = 'Fonte da sinopse · TMDB ↗';
    synopsis.append(source);
  }
  section.append(node('h3', '', 'Ficha técnica'));
  const facts = node('dl', 'tmdb-facts');
  function fact(label, value) {
    if (value) facts.append(node('dt', '', label), node('dd', '', value));
  }
  if (tmdb.originalTitle !== movie.title) fact('Título original', tmdb.originalTitle);
  fact('Gêneros', tmdb.genres.join(' · '));
  fact('Duração', tmdb.minutes > 0 ? tmdb.minutes + ' min' : '');
  fact('Direção', tmdb.directors.join(', '));
  fact('Roteiro', tmdb.writers.join(', '));
  fact('Criação', tmdb.creators.join(', '));
  section.append(facts);
  if (tmdb.cast.length) {
    section.append(node('h3', '', 'Elenco'));
    const cast = node('ul', 'tmdb-cast');
    for (const person of tmdb.cast) {
      const item = node('li');
      item.append(node('strong', '', person.name));
      if (person.role) item.append(node('span', '', person.role));
      cast.append(item);
    }
    section.append(cast);
  }
  section.append(attribution);
}

function renderMovieTrailer(section, actions, movie, id) {
  actions.append(button(id ? '▶ Ver trailer' : 'Buscar trailer', 'secondary', () => {
    if (id && !section.querySelector('iframe')) {
      const iframe = node('iframe');
      iframe.title = 'Trailer de ' + movie.title;
      iframe.allow = 'fullscreen; encrypted-media';
      iframe.allowFullscreen = true;
      iframe.referrerPolicy = 'origin';
      iframe.src = 'https://www.youtube-nocookie.com/embed/' + id;
      section.insertBefore(iframe, section.querySelector('a'));
    }
    section.scrollIntoView({ block: 'nearest' });
    section.querySelector('iframe, a')?.focus({ preventScroll: true });
  }));
  if (section.dataset.trailer === (id || '') && section.childElementCount) return;
  section.dataset.trailer = id || '';
  const link = node('a', 'detail-source', id ? 'Abrir trailer no YouTube ↗' : 'Pesquisar trailer no YouTube ↗');
  link.href = id ? 'https://www.youtube.com/watch?v=' + id :
    'https://www.youtube.com/results?search_query=' + encodeURIComponent([movie.title, movie.year, 'trailer oficial'].filter(Boolean).join(' '));
  link.target = '_blank';
  link.rel = 'noopener noreferrer';
  section.replaceChildren(node('h3', '', 'Trailer'));
  if (!id) section.append(node('p', '', 'Nenhum trailer vinculado. Você pode pesquisar ou adicionar um link em Editar detalhes.'));
  section.append(link);
}

async function catalog(page, limit, signal) {
  const query = new URLSearchParams({ section: state.section, search: state.search, page, limit });
  return (await request('/api/catalog?' + query, { signal })).json();
}

let fetchCatalog;
let firstPage;
async function refresh(append = false, keepPages = false) {
  if (state.section === 'downloads') return true;
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
    if (focused && document.activeElement === document.body) $('[data-movie="' + focused + '"] button')?.focus({ preventScroll: true });
    if ($('#details').open && detailId) {
      const updated = state.movies.find(movie => movie.id === detailId);
      if (updated) showDetails(updated);
    }
    $('#main').setAttribute('aria-busy', 'false');
    return true;
  } catch (error) {
    if (!current.signal.aborted) $('#status').textContent = error.message;
  } finally {
    if (fetchCatalog === current) state.busy = false;
  }
}

function changeSection(section) {
  fetchCatalog?.abort();
  state.section = section;
  state.search = '';
  $('#search').value = '';
  for (const tab of document.querySelectorAll('nav button')) {
    if (tab.dataset.section === section) tab.setAttribute('aria-current', 'page');
    else tab.removeAttribute('aria-current');
  }
  window.scrollTo(0, 0);
  const downloads = section === 'downloads';
  $('#downloads').hidden = downloads === false;
  $('#collection').hidden = downloads;
  $('.search').hidden = downloads;
  if (downloads) {
    $('#hero').hidden = true;
    heroKey = undefined;
    $('#main').setAttribute('aria-busy', 'false');
    refreshTransfers(true);
  } else {
    refresh();
  }
}

let removeAction;
function confirmRemoval(title, description, action) {
  $('#remove-title').textContent = title;
  $('#remove-description').textContent = description;
  $('#remove-status').textContent = '';
  removeAction = action;
  $('#remove-dialog').showModal();
}
$('#cancel-remove').onclick = () => $('#remove-dialog').close();
$('#confirm-remove').onclick = async () => {
  const confirm = $('#confirm-remove');
  confirm.disabled = $('#cancel-remove').disabled = true;
  try {
    await removeAction();
    $('#remove-dialog').close();
  } catch (error) {
    $('#remove-status').textContent = error.message;
  } finally { confirm.disabled = $('#cancel-remove').disabled = false; }
};
$('#remove-dialog').addEventListener('cancel', event => {
  if ($('#confirm-remove').disabled) event.preventDefault();
});

function renderUpdate(data) {
  const info = data.update || { stage: 'idle' };
  const active = ['check', 'download', 'verify'].includes(info.stage);
  const button = $('#update');
  button.hidden = !data.canUpdate || (active && data.updateRequested);
  button.textContent = info.stage === 'ready' ? 'Instalar e reiniciar' : 'Atualizar Cinevo';
  $('#update-transfer').hidden = !active;
  const known = info.stage === 'download' && info.total > 0;
  if (known) $('#update-progress').value = Math.min(100, info.received * 100 / info.total);
  else $('#update-progress').removeAttribute('value');
  $('#update-label').textContent = info.stage === 'check' ? 'Verificando atualização…' :
    info.stage === 'verify' ? 'Verificando integridade…' :
    info.stage === 'download' ? 'Baixando atualização · ' + storageSize(info.received) +
      (known ? ' de ' + storageSize(info.total) + ' (' + Math.floor($('#update-progress').value) + '%)' : '') : '';
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
$('#details').addEventListener('close', () => {
  if ($('#details').open) return;
  detailId = null;
  $('#detail-content').replaceChildren();
});
$('#clear-library').onclick = () => {
  $('details.menu').open = false;
  $('#clear-status').textContent = '';
  $('#clear-dialog').showModal();
};
$('#cancel-clear').onclick = () => $('#clear-dialog').close();
$('#confirm-clear').onclick = async () => {
  $('#confirm-clear').disabled = $('#cancel-clear').disabled = true;
  try {
    await request('/api/action', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: 'clear-library' })
    });
    $('#clear-dialog').close();
    changeSection('library');
  } catch (error) { $('#clear-status').textContent = error.message; }
  finally { $('#confirm-clear').disabled = $('#cancel-clear').disabled = false; }
};
$('#clear-dialog').addEventListener('cancel', event => {
  if ($('#confirm-clear').disabled) event.preventDefault();
});
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
    renderUpdate(data);
    $('#scan').textContent = data.scanning ? 'Cancelar busca de arquivos' : 'Atualizar arquivos';
    $('#scan').dataset.command = data.scanning ? 'cancel-scan' : 'scan';
    if (state.revision === data.revision) return;
    if (await refresh(false, true)) state.revision = data.revision;
  } catch { /* A janela pode estar fechando. */ }
}
async function poll(initial = false) {
  await checkRevision(initial);
  setTimeout(poll, $('#update-transfer').hidden ? 1800 : 550);
}
if (/^[0-9A-F]{64}$/.test(key || '')) {
  poll(true);
} else {
  $('#status').textContent = 'Abra a interface pelo aplicativo Cinevo.';
}
