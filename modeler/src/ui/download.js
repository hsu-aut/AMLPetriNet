/**
 * Handing a file to whoever is reading.
 *
 * In a browser that is a download. In a host that embeds the modeler it may be
 * a save dialogue, a folder beside the document, or a tab, and the host is the
 * only one that knows: so the file is offered on the event bus first, and a
 * host that says it took care of it gets no download on top.
 *
 * Nothing here knows what is in the file.
 */

/**
 * A file name that says which net the file came from.
 *
 * Three files land in one download folder, and "markings.csv" from two nets is
 * two files called markings.csv and markings(1).csv. The net's name is reduced
 * to what every file system takes, and a net without a name falls back to its
 * id and then to the word net.
 */
export function nameFor(canvas, suffix) {
  const root = canvas.getRootElement?.();
  const called = root?.businessObject?.name || root?.businessObject?.id || root?.id || 'net';

  const slug = String(called)
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 40);

  return `${slug || 'net'}-${suffix}`;
}

/**
 * @returns 'host' where a host took it, 'download' where the browser did,
 *   'failed' where neither could.
 */
export function offerFile(eventBus, file) {
  // A listener that returns true has saved the file itself. diagram-js stops at
  // the first listener that returns something, which is what makes this work.
  if (eventBus.fire('export.file', file) === true) return 'host';

  try {
    download(file);
    return 'download';
  } catch {
    return 'failed';
  }
}

function download({ name, text, type = 'text/plain;charset=utf-8' }) {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.rel = 'noopener';

  // In the document rather than detached: Firefox ignores a click on a link
  // that is not in it.
  document.body.appendChild(link);
  link.click();
  link.remove();

  // Long enough for the download to have started, short enough not to hold the
  // memory for the rest of the session.
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}
