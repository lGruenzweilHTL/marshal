import { inject, type InjectionKey } from 'vue'

/** WebAuthn ceremonies in the browser; options and credentials travel as the JSON the server speaks. */
export interface Passkeys {
  supported(): boolean
  /** Runs `navigator.credentials.get` for request options JSON and returns the credential's `toJSON()` as text. */
  get(optionsJson: string): Promise<string>
  /** Runs `navigator.credentials.create` for creation options JSON and returns the credential's `toJSON()` as text. */
  create(optionsJson: string): Promise<string>
}

export const passkeysKey: InjectionKey<Passkeys> = Symbol('passkeys')

/** Passkeys through the browser's WebAuthn JSON helpers (`parse*OptionsFromJSON`, `PublicKeyCredential.toJSON`). */
export const browserPasskeys: Passkeys = {
  supported: () => typeof PublicKeyCredential !== 'undefined' && typeof PublicKeyCredential.parseRequestOptionsFromJSON === 'function',
  async get(optionsJson) {
    const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(optionsJson) as PublicKeyCredentialRequestOptionsJSON)
    return serialize(await navigator.credentials.get({ publicKey }))
  },
  async create(optionsJson) {
    const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(optionsJson) as PublicKeyCredentialCreationOptionsJSON)
    return serialize(await navigator.credentials.create({ publicKey }))
  },
}

function serialize(credential: Credential | null): string {
  if (!(credential instanceof PublicKeyCredential)) throw new Error('No passkey was selected.')
  return JSON.stringify(credential.toJSON())
}

export function usePasskeys(): Passkeys {
  return inject(passkeysKey, browserPasskeys)
}
