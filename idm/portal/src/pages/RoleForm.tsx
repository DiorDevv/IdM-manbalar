import { useEffect, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '../api/client'
import type { RiskLevel, RoleDetails, SaveRole } from '../api/types'
import { t } from '../i18n'
import { Field, Modal, fieldError, formError, risks } from '../components/forms'

const r = t.roles
const empty: SaveRole = { code: '', name: '', description: '', risk: 'Medium', isRequestable: true }

/** Rol yaratish yoki tahrirlash oynasi. Xatolar server validatsiyasidan maydon yonida ko'rsatiladi. */
export function RoleForm({ open, role, onClose, onSaved }: { open: boolean; role?: RoleDetails; onClose: () => void; onSaved: (role: RoleDetails) => void }) {
  const [form, setForm] = useState<SaveRole>(empty)
  useEffect(() => {
    if (open) setForm(role ? { code: role.code, name: role.name, description: role.description ?? '', risk: role.risk, isRequestable: role.isRequestable } : empty)
  }, [open, role])
  const save = useMutation({
    mutationFn: (body: SaveRole) => api<RoleDetails>(role ? `/api/roles/${role.id}` : '/api/roles', { method: role ? 'PUT' : 'POST', body: JSON.stringify(body) }),
    onSuccess: onSaved,
  })
  // Oyna qayta ochilganda oldingi xato ko'rinmasin
  useEffect(() => { if (open) save.reset() }, [open])
  const set = <K extends keyof SaveRole>(key: K, value: SaveRole[K]) => setForm(f => ({ ...f, [key]: value }))
  const general = formError(save.error, ['code', 'name', 'description'])

  return (
    <Modal open={open} title={role ? r.edit : r.create} onClose={onClose} footer={<>
      <button className="btn secondary" onClick={onClose}>{t.cancel}</button>
      <button className="btn primary" disabled={save.isPending} onClick={() => save.mutate({ ...form, description: form.description?.trim() || null })}>
        {save.isPending ? r.saving : r.save}
      </button>
    </>}>
      <form className="form" onSubmit={e => { e.preventDefault(); save.mutate({ ...form, description: form.description?.trim() || null }) }}>
        {general && <div className="alert bad">{general}</div>}
        <div className="form-row">
          <Field label={r.fields.code} error={fieldError(save.error, 'code')} help={r.codeHelp}>
            <input className="input mono" value={form.code} maxLength={64} onChange={e => set('code', e.target.value.toUpperCase().replace(/\s+/g, '-'))}
              aria-invalid={!!fieldError(save.error, 'code')} autoComplete="off" />
          </Field>
          <Field label={r.fields.risk}>
            <select className="select" value={form.risk} onChange={e => set('risk', e.target.value as RiskLevel)}>
              {risks.map(x => <option key={x} value={x}>{t.risk[x]}</option>)}
            </select>
          </Field>
        </div>
        <Field label={r.fields.name} error={fieldError(save.error, 'name')}>
          <input className="input" value={form.name} maxLength={256} onChange={e => set('name', e.target.value)} aria-invalid={!!fieldError(save.error, 'name')} />
        </Field>
        <Field label={r.fields.description} error={fieldError(save.error, 'description')}>
          <textarea className="input" value={form.description ?? ''} maxLength={2000} onChange={e => set('description', e.target.value)} />
        </Field>
        <label className="switch"><input type="checkbox" checked={form.isRequestable} onChange={e => set('isRequestable', e.target.checked)} />{r.requestable}</label>
        <button type="submit" hidden />
      </form>
    </Modal>
  )
}
