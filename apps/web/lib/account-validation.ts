export const passwordRules = [
    { key: "length", label: "12–128 characters", test: (value: string) => value.length >= 12 && value.length <= 128 },
    { key: "uppercase", label: "An uppercase letter (A–Z)", test: (value: string) => /[A-Z]/.test(value) },
    { key: "lowercase", label: "A lowercase letter (a–z)", test: (value: string) => /[a-z]/.test(value) },
    { key: "number", label: "A number (0–9)", test: (value: string) => /[0-9]/.test(value) },
    { key: "symbol", label: "A symbol (for example ! or #)", test: (value: string) => /[^a-zA-Z0-9]/.test(value) },
];

export function passwordFeedback(value: string) {
    const checks = passwordRules.map(rule => ({ key: rule.key, label: rule.label, met: rule.test(value) }));
    const score = checks.filter(check => check.met).length;
    const valid = checks.every(check => check.met);
    const level = !value ? "empty" : valid ? "strong" : score >= 4 && value.length <= 128 ? "medium" : "weak";
    return { checks, score, valid, level };
}

export type AccountFields = { name: string; email: string; password: string; confirmPassword: string; accountType: string };
export type AccountErrors = Partial<Record<keyof AccountFields, string>>;
export function validateAccount(fields: AccountFields, register: boolean): AccountErrors {
    const errors: AccountErrors = {};
    if (register && (!fields.name.trim() || fields.name.length > 150)) errors.name = "Enter a name of 1–150 characters.";
    if (!fields.email.trim() || fields.email.trim().length > 254 || !/^[^\s@]+@[^\s@]+$/.test(fields.email.trim())) errors.email = "Enter a valid email address.";
    if (!fields.password) errors.password = "Enter your password.";
    else if (fields.password.length > 128) errors.password = "Use no more than 128 characters.";
    else if (register && !passwordFeedback(fields.password).valid) errors.password = "Meet all five password requirements below.";
    if (register && fields.confirmPassword !== fields.password) errors.confirmPassword = "Passwords do not match.";
    else if (register && !fields.confirmPassword) errors.confirmPassword = "Confirm your password.";
    if (register && !["Customer", "Provider"].includes(fields.accountType)) errors.accountType = "Choose Customer or Provider.";
    return errors;
}
