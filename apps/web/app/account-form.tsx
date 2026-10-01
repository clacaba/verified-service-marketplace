"use client";

import { useRef, useState, type FormEvent } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { authRequest } from "../lib/auth-client";
import { passwordFeedback, validateAccount, type AccountFields, type AccountErrors } from "../lib/account-validation";

function Required() { return <span className="required-marker" aria-hidden="true"> *</span>; }
function Eye({ visible }: { visible: boolean }) {
    return <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true"><path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/>{visible && <path d="m3 3 18 18"/>}</svg>;
}
const empty: AccountFields = { name: "", email: "", password: "", confirmPassword: "", accountType: "Customer" };

export default function AccountForm({ mode }: { mode: "register" | "login" }) {
    const register = mode === "register";
    const router = useRouter(); const formRef = useRef<HTMLFormElement>(null);
    const [fields, setFields] = useState<AccountFields>(empty);
    const [errors, setErrors] = useState<AccountErrors>({});
    const [touched, setTouched] = useState<Partial<Record<keyof AccountFields, boolean>>>({});
    const [busy, setBusy] = useState(false); const [message, setMessage] = useState(""); const [success, setSuccess] = useState(false);
    const [showPassword, setShowPassword] = useState(false); const [showConfirm, setShowConfirm] = useState(false);
    const strength = passwordFeedback(fields.password);
    function change(key: keyof AccountFields, value: string) {
        const next = { ...fields, [key]: value }; setFields(next);
        const validation = validateAccount(next, register);
        setErrors(current => ({ ...current, [key]: touched[key] ? validation[key] : undefined,
            ...(key === "password" && touched.confirmPassword ? { confirmPassword: validation.confirmPassword } : {}) }));
        setMessage(""); setSuccess(false);
    }
    function blur(key: keyof AccountFields) {
        setTouched(current => ({ ...current, [key]: true }));
        setErrors(current => ({ ...current, [key]: validateAccount(fields, register)[key] }));
    }
    function error(key: keyof AccountFields) { return errors[key] ? <p id={`${key}-error`} className="field-error">{errors[key]}</p> : null; }
    function attributes(key: keyof AccountFields) { return { "aria-invalid": Boolean(errors[key]), "aria-describedby": errors[key] ? `${key}-error` : undefined, onBlur: () => blur(key) }; }
    async function submit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault(); if (busy) return;
        const validation = validateAccount(fields, register);
        const emailInput = formRef.current?.elements.namedItem("email") as HTMLInputElement | null;
        if (emailInput && !emailInput.validity.valid) validation.email = "Enter a valid email address.";
        setErrors(validation); setTouched({ name: true, email: true, password: true, confirmPassword: true, accountType: true });
        if (Object.keys(validation).length) {
            (formRef.current?.elements.namedItem(Object.keys(validation)[0]) as HTMLElement | null)?.focus(); return;
        }
        setBusy(true); setMessage(""); setSuccess(false);
        const input = register ? { name: fields.name.trim(), email: fields.email.trim(), password: fields.password, accountType: fields.accountType }
            : { email: fields.email.trim(), password: fields.password };
        try {
            await authRequest("auth/" + mode, input);
            if (!register) { router.push("/account"); router.refresh(); }
            else {
                setSuccess(true); setMessage("Check your email for a verification link. You can sign in now to begin your profile.");
                setFields(empty); setTouched({}); setErrors({}); setShowPassword(false); setShowConfirm(false);
            }
        } catch (failure) { setMessage(failure instanceof Error ? failure.message : "Request failed. Please try again."); }
        finally { setBusy(false); }
    }
    return <main className="auth-page account-page">
        <Link className="auth-brand" href="/">Verified Service Marketplace</Link>
        <section className="account-card">
            <p className="eyebrow">{register ? "GET STARTED" : "YOUR MARKETPLACE"}</p>
            <h1>{register ? "Create your account" : "Welcome back"}</h1>
            <p>{register ? "Find services or build your provider profile." : "Sign in to continue your marketplace journey."}</p>
            <p className="required-note">Fields marked <span className="required-marker">*</span> are required.</p>
            <form ref={formRef} onSubmit={submit} noValidate aria-busy={busy}>
                <fieldset className="account-fields" disabled={busy}>
                    {register && <>
                        <div className="form-field"><label htmlFor="name">Name<Required/></label><input id="name" name="name" autoComplete="name" required maxLength={150} value={fields.name} onChange={event => change("name", event.target.value)} {...attributes("name")}/>{error("name")}</div>
                        <div className="form-field"><label htmlFor="accountType">Account type<Required/></label><select id="accountType" name="accountType" required value={fields.accountType} onChange={event => change("accountType", event.target.value)} {...attributes("accountType")}><option value="Customer">Customer — book a service</option><option value="Provider">Provider — offer services</option></select>{error("accountType")}</div>
                    </>}
                    <div className="form-field"><label htmlFor="email">Email<Required/></label><input id="email" name="email" type="email" autoComplete="email" required maxLength={254} value={fields.email} onChange={event => change("email", event.target.value)} {...attributes("email")}/>{error("email")}</div>
                    <div className="form-field"><label htmlFor="password">Password<Required/></label>
                        <div className="password-control"><input id="password" name="password" type={showPassword ? "text" : "password"} autoComplete={register ? "new-password" : "current-password"} required minLength={register ? 12 : 1} maxLength={128} value={fields.password} onChange={event => change("password", event.target.value)} {...attributes("password")} aria-describedby={[errors.password ? "password-error" : "", register ? "password-guidance" : ""].filter(Boolean).join(" ") || undefined}/>
                            <button className="password-toggle" type="button" aria-label={showPassword ? "Hide password" : "Show password"} aria-controls="password" aria-pressed={showPassword} onClick={() => setShowPassword(value => !value)}><Eye visible={showPassword}/></button></div>{error("password")}
                        {register && <div id="password-guidance" className={`password-feedback strength-${strength.level}`}>
                            <p className="strength-label" role="status">{fields.password ? `Password strength: ${strength.level === "strong" ? "Strong" : strength.level === "medium" ? "Medium" : "Weak"}` : "Choose a password"}</p>
                            <div className="strength-track" aria-hidden="true"><span style={{ width: `${strength.score * 20}%` }}/></div>
                            <small>This checks the requirements, not whether a password is unique or compromised. Use a unique password or passphrase.</small>
                            <ul className="password-checklist">{strength.checks.map(check => <li key={check.key} className={check.met ? "met" : "unmet"}><span aria-hidden="true">{check.met ? "✓" : "○"}</span><span className="sr-only">{check.met ? "Met: " : "Not met: "}</span> {check.label}</li>)}</ul>
                        </div>}
                    </div>
                    {register && <div className="form-field"><label htmlFor="confirmPassword">Confirm password<Required/></label><div className="password-control"><input id="confirmPassword" name="confirmPassword" type={showConfirm ? "text" : "password"} autoComplete="new-password" required maxLength={128} value={fields.confirmPassword} onChange={event => change("confirmPassword", event.target.value)} {...attributes("confirmPassword")}/><button className="password-toggle" type="button" aria-label={showConfirm ? "Hide confirm password" : "Show confirm password"} aria-controls="confirmPassword" aria-pressed={showConfirm} onClick={() => setShowConfirm(value => !value)}><Eye visible={showConfirm}/></button></div>{error("confirmPassword")}</div>}
                    <button className="account-submit" type="submit" disabled={busy}>{busy ? "Please wait…" : register ? "Create account" : "Sign in"}</button>
                </fieldset>
                {message && <p className={success ? "form-message success" : "form-message failure"} role={success ? "status" : "alert"}>{message}</p>}
            </form>
            <Link className="auth-switch" href={register ? "/login" : "/register"}>{register ? "Already have an account? Sign in" : "New here? Create an account"}</Link>
        </section>
    </main>;
}
