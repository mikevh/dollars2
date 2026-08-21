// Bridges the server's WebAuthn options JSON (ASP.NET Core Identity's IPasskeyHandler) to the
// browser's native passkey ceremony. Uses the browser's own JSON <-> WebAuthn conversion
// (PublicKeyCredential.parse*OptionsFromJSON / credential.toJSON()) rather than a JS library, since
// modern browsers implement the WebAuthn Level 3 JSON serialization these endpoints already speak.

export interface PasskeyOptionsResponse {
  optionsJson: string
}

export async function performPasskeyCreation(optionsJson: string): Promise<string> {
  const options = PublicKeyCredential.parseCreationOptionsFromJSON(JSON.parse(optionsJson))
  const credential = await navigator.credentials.create({ publicKey: options }) as PublicKeyCredential | null
  if (!credential) {
    throw new Error('Passkey registration was cancelled.')
  }
  return JSON.stringify(credential.toJSON())
}

export async function performPasskeyAssertion(optionsJson: string): Promise<string> {
  const options = PublicKeyCredential.parseRequestOptionsFromJSON(JSON.parse(optionsJson))
  const credential = await navigator.credentials.get({ publicKey: options }) as PublicKeyCredential | null
  if (!credential) {
    throw new Error('Passkey sign-in was cancelled.')
  }
  return JSON.stringify(credential.toJSON())
}
