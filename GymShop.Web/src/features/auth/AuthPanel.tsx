import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../../api/gymshop'
import type { AuthResponse, MfaSetup, User } from '../../api/types'
import { storefront } from '../../config/storefront'
import { authErrorMessage } from './authMessages'
import { ApiError } from '../../api/client'
import {
  clearPendingRegistration,
  loadPendingRegistration,
  remainingSeconds,
  savePendingRegistration,
  type PendingRegistration,
} from './pendingRegistration'

interface AuthPanelProps {
  onDone(auth: AuthResponse): void
}

let initializedGoogleClientId: string | null = null
let googleCredentialHandler: ((credential?: string) => void) | null = null

function PasswordVisibilityIcon({ visible }: { visible: boolean }) {
  return <svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">
    <path d="M2.5 12s3.5-6 9.5-6 9.5 6 9.5 6-3.5 6-9.5 6-9.5-6-9.5-6Z" />
    <circle cx="12" cy="12" r="2.75" />
    {visible && <path d="m4 4 16 16" />}
  </svg>
}

export function AuthPanel({ onDone }: AuthPanelProps) {
  const [register, setRegister] = useState(false)
  const [forgot, setForgot] = useState(false)
  const [resetEmail, setResetEmail] = useState<string | null>(null)
  const [resetSeconds, setResetSeconds] = useState(0)
  const [showPassword, setShowPassword] = useState(false)
  const [pending, setPending] = useState<PendingRegistration | null>(() => loadPendingRegistration())
  const [developmentCode, setDevelopmentCode] = useState<string | null>(null)
  const [seconds, setSeconds] = useState(() => pending ? remainingSeconds(pending) : 0)
  const [busy, setBusy] = useState(false)
  const busyRef = useRef(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState(() => pending ? 'Retomamos tu verificación pendiente.' : '')
  const [pendingGoogleCredential, setPendingGoogleCredential] = useState<string | null>(null)
  const [mfaUser, setMfaUser] = useState<User | null>(null)
  const [mfaSetup, setMfaSetup] = useState<MfaSetup | null>(null)
  const [mfaRecoveryCodes, setMfaRecoveryCodes] = useState<string[]>([])
  const googleButton = useRef<HTMLDivElement>(null)
  const googleConfigured = Boolean(import.meta.env.VITE_GOOGLE_CLIENT_ID && !import.meta.env.VITE_GOOGLE_CLIENT_ID.startsWith('<'))

  const complete = useCallback((auth: AuthResponse) => {
    clearPendingRegistration()
    onDone(auth)
  }, [onDone])

  const handleAuthentication = useCallback(async (auth: AuthResponse) => {
    if (!auth.mfaRequired) return complete(auth)
    setMfaUser(auth.user)
    if (auth.setupRequired) setMfaSetup(await api.mfaSetup())
  }, [complete])

  const execute = useCallback(async (action: () => Promise<void>) => {
    if (busyRef.current) return
    busyRef.current = true
    setBusy(true)
    setError('')
    setNotice('')
    try {
      await action()
    } catch (value) {
      setError(authErrorMessage(value))
    } finally {
      busyRef.current = false
      setBusy(false)
    }
  }, [])

  useEffect(() => {
    if (!pending) return
    setSeconds(remainingSeconds(pending))
    const timer = window.setInterval(() => setSeconds(remainingSeconds(pending)), 1000)
    return () => window.clearInterval(timer)
  }, [pending])

  useEffect(() => {
    if (!resetEmail || resetSeconds <= 0) return
    const timer = window.setInterval(() => setResetSeconds(value => Math.max(0, value - 1)), 1000)
    return () => window.clearInterval(timer)
  }, [resetEmail])

  useEffect(() => {
    const clientId = import.meta.env.VITE_GOOGLE_CLIENT_ID
    if (register || forgot || resetEmail || !googleConfigured || !clientId) return
    let attempts = 0
    let cancelled = false
    let retryTimer: number | undefined
    const render = () => {
      if (cancelled) return
      if (window.google && googleButton.current) {
        googleButton.current.replaceChildren()
        googleCredentialHandler = credential => {
          if (!credential) {
            setError('')
            setNotice('Cancelaste el acceso con Google. Podés intentarlo nuevamente o ingresar con email y contraseña.')
            return
          }
          void execute(async () => {
            try {
              await handleAuthentication(await api.googleLogin(credential))
            } catch (value) {
              if (value instanceof ApiError && value.code === 'google_link_required') {
                setPendingGoogleCredential(credential)
                setNotice('Tu email ya tiene una cuenta. Ingresá su contraseña una única vez para vincular Google.')
                return
              }
              throw value
            }
          })
        }
        if (initializedGoogleClientId !== clientId) {
          window.google.accounts.id.initialize({
            client_id: clientId,
            callback: ({ credential }) => googleCredentialHandler?.(credential),
          })
          initializedGoogleClientId = clientId
        }
        window.google.accounts.id.renderButton(googleButton.current, { theme: 'outline', size: 'large', width: 320 })
      } else if (attempts++ < 20) {
        retryTimer = window.setTimeout(render, 250)
      } else {
        setError('No se pudo cargar el acceso con Google. Revisá la conexión e intentá nuevamente.')
      }
    }
    render()
    return () => {
      cancelled = true
      if (retryTimer !== undefined) window.clearTimeout(retryTimer)
    }
  }, [execute, forgot, googleConfigured, handleAuthentication, register, resetEmail])

  if (mfaRecoveryCodes.length > 0 && mfaUser) {
    return <section className="auth-card">
      <div><p className="eyebrow">CÓDIGOS DE RECUPERACIÓN</p><h1>Guardalos ahora</h1><p>Cada código sirve una sola vez si perdés acceso a tu aplicación autenticadora.</p></div>
      <div className="mfa-recovery-codes">{mfaRecoveryCodes.map(code => <code key={code}>{code}</code>)}</div>
      <button className="primary" type="button" onClick={() => complete({ user: mfaUser })}>Ya los guardé</button>
    </section>
  }

  if (mfaUser) {
    return <section className="auth-card">
      <div><p className="eyebrow">SEGURIDAD ADMINISTRATIVA</p><h1>{mfaSetup ? 'Configurá la autenticación en dos pasos' : 'Ingresá tu segundo factor'}</h1><p>{mfaSetup ? 'Escaneá el código con una aplicación TOTP o cargá la clave manualmente.' : 'Usá el código de tu aplicación autenticadora o un código de recuperación.'}</p>{mfaSetup && <><div className="mfa-qr" aria-label="Código QR TOTP">{mfaSetup.qrCodeRows.map((row, index) => <div key={index}>{row.replaceAll('1', '██').replaceAll('0', '  ')}</div>)}</div><p>Clave manual: <code>{mfaSetup.sharedKey}</code></p></>}</div>
      <form onSubmit={event => { event.preventDefault(); const code = String(new FormData(event.currentTarget).get('code')); void execute(async () => { const result = mfaSetup ? await api.mfaEnable(code) : await api.mfaComplete(code); if (result.recoveryCodes.length) setMfaRecoveryCodes(result.recoveryCodes); else complete({ user: result.user }) }) }}>
        {error && <div className="error" role="alert">{error}</div>}<label>Código<input name="code" inputMode="numeric" autoComplete="one-time-code" minLength={6} maxLength={32} required autoFocus disabled={busy} /></label><button className="primary" type="submit" disabled={busy}>{busy ? 'Verificando…' : 'Verificar e ingresar'}</button>
      </form>
    </section>
  }

  if (pending) {
    return <section className="auth-card">
      <div>
        <p className="eyebrow">VERIFICÁ TU EMAIL</p>
        <h1>Ingresá los 6 números</h1>
        <p>Generamos un código de verificación para <strong>{pending.email}</strong>.</p>
        <p>{seconds > 0 ? `Vence en ${seconds} segundos.` : 'El código venció. Ya podés solicitar uno nuevo.'}</p>
        {developmentCode && <p className="mock-code">{storefront.copy.localCodeLabel}: <strong>{developmentCode}</strong></p>}
      </div>
      <form onSubmit={event => {
        event.preventDefault()
        const code = String(new FormData(event.currentTarget).get('code'))
        void execute(async () => handleAuthentication(await api.verifyEmail({ email: pending.email, code })))
      }}>
        {notice && <div className="notice" role="status">{notice}</div>}
        {error && <div className="error" role="alert">{error}</div>}
        <label>Código de verificación
          <input name="code" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" minLength={6} maxLength={6} required autoFocus disabled={busy || seconds === 0} />
        </label>
        <button className="primary" type="submit" disabled={busy || seconds === 0}>{busy ? 'Verificando…' : 'Verificar e ingresar'}</button>
        <button type="button" disabled={busy || seconds > 0} onClick={() => void execute(async () => {
          const result = await api.resendVerification(pending.email)
          setPending(savePendingRegistration(result.email, result.expiresInSeconds))
          setDevelopmentCode(result.developmentCode ?? null)
          setNotice('Solicitamos el envío de un código nuevo.')
        })}>{busy ? 'Enviando…' : 'Reenviar código'}</button>
        <button type="button" className="link" disabled={busy} onClick={() => {
          clearPendingRegistration()
          setPending(null)
          setDevelopmentCode(null)
          setError('')
          setNotice('')
        }}>Cambiar email</button>
      </form>
    </section>
  }

  if (resetEmail) {
    return <section className="auth-card">
      <div><p className="eyebrow">RECUPERÁ TU CUENTA</p><h1>Creá una contraseña nueva</h1><p>Ingresá el código de 6 dígitos enviado a <strong>{resetEmail}</strong>.</p><p>{resetSeconds > 0 ? `El código vence en ${Math.ceil(resetSeconds / 60)} minutos.` : 'El código puede haber vencido. Podés solicitar uno nuevo.'}</p>{developmentCode && <p className="mock-code">{storefront.copy.localCodeLabel}: <strong>{developmentCode}</strong></p>}</div>
      <form onSubmit={event => { event.preventDefault(); const data = new FormData(event.currentTarget); void execute(async () => {
        const result = await api.resetPassword({ email: resetEmail, code: String(data.get('code')), newPassword: String(data.get('newPassword')) })
        setResetEmail(null); setForgot(false); setDevelopmentCode(null); setResetSeconds(0); setNotice(result.message)
      }) }}>
        {notice && <div className="notice" role="status">{notice}</div>}{error && <div className="error" role="alert">{error}</div>}
        <fieldset disabled={busy}><label>Código de recuperación<input key="password-reset-code" name="code" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" minLength={6} maxLength={6} required autoFocus /></label><label>Nueva contraseña<div className="password-field"><input name="newPassword" type={showPassword ? 'text' : 'password'} required minLength={8} maxLength={128} autoComplete="new-password" /><button className="password-visibility" type="button" aria-label={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'} aria-pressed={showPassword} onClick={() => setShowPassword(value => !value)}><PasswordVisibilityIcon visible={showPassword} /></button></div></label></fieldset>
        <button className="primary" type="submit" disabled={busy}>{busy ? 'Actualizando…' : 'Cambiar contraseña'}</button>
        <button type="button" className="link" disabled={busy} onClick={() => { setResetEmail(null); setForgot(true); setDevelopmentCode(null); setError(''); setNotice('') }}>Solicitar otro código</button>
        <button type="button" className="link" disabled={busy} onClick={() => { setResetEmail(null); setForgot(false); setDevelopmentCode(null); setError(''); setNotice('') }}>Volver al login</button>
      </form>
    </section>
  }

  if (forgot) {
    return <section className="auth-card">
      <div><p className="eyebrow">RECUPERÁ TU CUENTA</p><h1>¿Olvidaste tu contraseña?</h1><p>Ingresá tu email. Si corresponde a una cuenta, enviaremos un código de 6 dígitos válido durante 10 minutos.</p></div>
      <form onSubmit={event => { event.preventDefault(); const email = String(new FormData(event.currentTarget).get('email')).trim().toLowerCase(); void execute(async () => {
        const result = await api.forgotPassword(email); setResetEmail(email); setResetSeconds(result.expiresInSeconds); setDevelopmentCode(result.developmentCode ?? null); setNotice(result.message)
      }) }}>
        {notice && <div className="notice" role="status">{notice}</div>}{error && <div className="error" role="alert">{error}</div>}
        <fieldset disabled={busy}><label>Email<input name="email" type="email" required maxLength={256} autoComplete="email" autoFocus /></label></fieldset>
        <button className="primary" type="submit" disabled={busy}>{busy ? 'Enviando…' : 'Enviar código'}</button>
        <button type="button" className="link" disabled={busy} onClick={() => { setForgot(false); setError(''); setNotice('') }}>Volver al login</button>
      </form>
    </section>
  }

  return <section className="auth-card">
    <div>
      <p className="eyebrow">TU CUENTA</p>
      <h1>{register ? storefront.copy.registrationTitle : 'Bienvenido de nuevo'}</h1>
      <p>Accedé a tu carrito, pagos y seguimiento de órdenes.</p>
    </div>
    <form onSubmit={event => {
      event.preventDefault()
      const data = new FormData(event.currentTarget)
      void execute(async () => {
        const email = String(data.get('email')).trim().toLowerCase()
        if (register) {
          const result = await api.register({
            name: String(data.get('name')),
            lastName: String(data.get('lastName')),
            email,
            password: String(data.get('password')),
          })
          setPending(savePendingRegistration(result.email, result.expiresInSeconds))
          setDevelopmentCode(result.developmentCode ?? null)
          setNotice('Cuenta creada. Solicitamos el envío del código para activarla.')
        } else {
          const localAuth = await api.login({ email, password: String(data.get('password')) })
          if (pendingGoogleCredential) {
            await handleAuthentication(await api.googleLogin(pendingGoogleCredential))
          } else {
            await handleAuthentication(localAuth)
          }
        }
      })
    }}>
      {notice && <div className="notice" role="status">{notice}</div>}
      {error && <div className="error" role="alert">{error}</div>}
      <fieldset disabled={busy}>
        {register && <div className="name-fields">
          <label>Nombre<input name="name" required maxLength={100} autoComplete="given-name" /></label>
          <label>Apellido<input name="lastName" required maxLength={100} autoComplete="family-name" /></label>
        </div>}
        <label>Email<input name="email" type="email" required maxLength={256} autoComplete="email" /></label>
        <label>Contraseña
          <div className="password-field">
            <input name="password" type={showPassword ? 'text' : 'password'} required minLength={register ? 8 : undefined} maxLength={128} autoComplete={register ? 'new-password' : 'current-password'} />
            <button className="password-visibility" type="button" aria-label={showPassword ? 'Ocultar contraseña' : 'Mostrar contraseña'} aria-pressed={showPassword} onClick={() => setShowPassword(value => !value)}><PasswordVisibilityIcon visible={showPassword} /></button>
          </div>
        </label>
      </fieldset>
      <button className="primary" type="submit" disabled={busy}>{busy ? (register ? 'Creando cuenta…' : 'Ingresando…') : (register ? 'Crear cuenta' : 'Ingresar')}</button>
      {!register && <button type="button" className="link" disabled={busy} onClick={() => { setForgot(true); setError(''); setNotice('') }}>Olvidé mi contraseña</button>}
      <button type="button" className="link" disabled={busy} onClick={() => { setRegister(value => !value); setError(''); setNotice('') }}>{register ? 'Ya tengo cuenta' : 'Quiero registrarme'}</button>
      <div className="auth-divider"><span>o</span></div>
      <div ref={googleButton} className="google-button" aria-label="Acceso con Google">
        {!googleConfigured && <small>Google no está configurado en este ambiente. Definí VITE_GOOGLE_CLIENT_ID para habilitarlo.</small>}
      </div>
    </form>
  </section>
}
