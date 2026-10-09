import { storefront } from '../../config/storefront'

type SocialIconName = 'instagram' | 'facebook' | 'gmail'

const socialLinks: Array<{ name: string; icon: SocialIconName; href: string }> = [
  { name: 'Instagram', icon: 'instagram', href: storefront.social.instagramUrl },
  { name: 'Facebook', icon: 'facebook', href: storefront.social.facebookUrl },
  { name: 'Gmail', icon: 'gmail', href: storefront.social.email ? `mailto:${storefront.social.email}` : '' },
]

function SocialIcon({ name }: { name: SocialIconName }) {
  if (name === 'instagram') return <svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="5" /><circle cx="12" cy="12" r="4" /><circle className="social-icon-dot" cx="17.5" cy="6.5" r="1" /></svg>
  if (name === 'facebook') return <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M14 8h3V4h-3c-3.3 0-5 2-5 5v2H6v4h3v6h4v-6h3.3l.7-4h-4V9c0-.7.3-1 1-1Z" /></svg>
  return <svg className="gmail-icon" viewBox="0 0 24 24" aria-hidden="true"><path className="gmail-red" d="M3 7.2 12 14l9-6.8" /><path className="gmail-blue" d="M3 7.2V19" /><path className="gmail-green" d="M21 7.2V19" /><path className="gmail-yellow" d="M3 19h18" /></svg>
}

export function SocialLinks() {
  return <div className="footer-social" aria-label="Redes sociales">
    <span className="footer-social-label">Encontranos en</span>
    <div className="footer-social-icons">
      {socialLinks.map(link => link.href
        ? <a key={link.name} href={link.href} target={link.href.startsWith('mailto:') ? undefined : '_blank'} rel={link.href.startsWith('mailto:') ? undefined : 'noreferrer'} aria-label={link.name} title={link.name}><SocialIcon name={link.icon} /></a>
        : <span key={link.name} className="social-link-disabled" aria-label={`${link.name}, próximamente`} title={`${link.name} · próximamente`}><SocialIcon name={link.icon} /></span>)}
    </div>
  </div>
}
