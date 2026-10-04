import type { Catalog } from '../i18n/catalog'

export const en = {
  title: 'Chat',
  subtitle: 'Talk to Marshal in writing. Voice goes through Home Assistant Assist.',
  newChat: 'New chat',
  empty: { title: 'Ask Marshal anything', body: 'Lights, the printer, your notes, tasks: Marshal answers here like it does by voice.' },
  message: 'Message',
  placeholder: 'Ask Marshal…',
  send: 'Send',
  you: 'You',
  thinking: 'Thinking…',
  awaiting: 'Marshal is waiting for your answer.',
  yes: 'Yes',
  no: 'No',
  interrupted: 'The connection was interrupted before the reply was complete.',
  failed: 'The message could not be sent',
}

export const de: Catalog<typeof en> = {
  title: 'Chat',
  subtitle: 'Schreib mit Marshal. Sprache läuft über Home Assistant Assist.',
  newChat: 'Neuer Chat',
  empty: { title: 'Frag Marshal', body: 'Licht, Drucker, Notizen, Aufgaben: Marshal antwortet hier wie per Sprache.' },
  message: 'Nachricht',
  placeholder: 'Frag Marshal…',
  send: 'Senden',
  you: 'Du',
  thinking: 'Denkt nach…',
  awaiting: 'Marshal wartet auf deine Antwort.',
  yes: 'Ja',
  no: 'Nein',
  interrupted: 'Die Verbindung brach ab, bevor die Antwort fertig war.',
  failed: 'Die Nachricht konnte nicht gesendet werden',
}
