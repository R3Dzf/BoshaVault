#!/usr/bin/env python3
"""Generate the hosted Supabase Auth templates; no credentials or sending here."""
import html
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "supabase/templates"

# route, filename, subject, heading, category, paragraphs, token, notification enabled
CATALOG = [
    ("confirm-sign-up", "confirmation", "تأكيد بريدك في Lockvyn", "أكد بريدك الإلكتروني", "تأكيد الحساب", [
        'لإكمال إنشاء حسابك السحابي، أدخل الرمز في خانة <span dir="ltr">Email confirmation code</span> داخل Lockvyn، ثم اختر <span dir="ltr">Confirm email code</span>.',
        'بعد التأكيد، سجل الدخول بكلمة مرور حسابك السحابي. إذا فعّلت المصادقة الثنائية، سيطلب البرنامج كود تطبيق المصادقة أيضًا.',
        'إذا انتهت صلاحية الرمز، اختر <span dir="ltr">Resend confirmation email</span>. انتظر دقيقة بين طلبات الإرسال.',
        'إذا لم تطلب إنشاء هذا الحساب، تجاهل الرسالة.'
    ], True, None),
    ("reset-password", "recovery", "استعادة باسورد حسابك السحابي في Lockvyn", "استعد الوصول إلى حساب السحابة", "استعادة الحساب", [
        'وصلنا طلب لإعادة تعيين كلمة مرور حسابك السحابي. استخدم الرمز في مسار الاستعادة الذي طلبت منه هذه الرسالة.',
        'إعادة تعيين كلمة مرور السحابة لا تلغي المصادقة الثنائية، ولا تستعيد الباسورد الرئيسي لخزنتك المحلية.',
        'إذا لم تطلب الاستعادة، تجاهل الرسالة. لن تتغير كلمة مرورك بمجرد استلامها.'
    ], True, None),
    ("change-email-address", "email-change", "تأكيد تغيير بريد حساب Lockvyn", "أكد تغيير البريد", "تغيير البريد", [
        'طُلب تغيير بريد حسابك السحابي إلى <span dir="ltr">{{ .NewEmail }}</span>. استخدم الرمز في مسار تغيير البريد الذي بدأت منه الطلب.',
        'قد تحتاج تأكيد البريد القديم والجديد قبل اكتمال التغيير. لا تشارك أيًا من رمزي التأكيد.',
        'إذا لم تطلب تغيير البريد، لا تستخدم الرمز وراجع أمان حسابك.'
    ], True, None),
    ("reauthentication", "reauthentication", "تأكيد إجراء حساس في Lockvyn", "أكد أنك صاحب الحساب", "تأكيد إجراء", [
        'استخدم الرمز لتأكيد الإجراء الحساس الذي طلبته في حسابك السحابي.',
        'هذا الرمز لتأكيد الإجراء فقط. كود المصادقة الثنائية المعتاد يُؤخذ من تطبيق المصادقة.',
        'إذا لم تبدأ هذا الإجراء، لا تستخدم الرمز وراجع أمان حسابك.'
    ], True, None),
    ("magic-link-or-otp", "magic-link", "رمز الدخول إلى حساب Lockvyn", "رمز دخولك لمرة واحدة", "تسجيل الدخول", [
        'طُلب رمز دخول إلى حسابك السحابي. استخدمه في مسار تسجيل الدخول بالبريد الذي طلبت منه الرسالة.',
        'هذا ليس كود تطبيق المصادقة الثنائية، ولا يلغي طلبه إذا كانت 2FA مفعلة.',
        'إذا لم تطلب الدخول، تجاهل الرسالة ولا تشارك الرمز.'
    ], True, None),
    ("invite-user", "invite", "دعوة لإنشاء حساب Lockvyn", "دعوتك إلى Lockvyn", "دعوة حساب", [
        'أُرسلت إليك دعوة لإنشاء حساب سحابي في Lockvyn. استخدم الرمز في مسار قبول الدعوة إذا كنت تتوقعها.',
        'الدعوة لا تمنح المرسل الوصول إلى خزنتك أو كلمات مرورك. اختر كلمات مرور مختلفة للحساب والخزنة.',
        'إذا لم تكن تتوقع الدعوة، يمكنك تجاهل هذه الرسالة.'
    ], True, None),
    ("password-changed", "password-changed", "تنبيه Lockvyn: تغيّر باسورد حساب السحابة", "تم تغيير كلمة مرور الحساب", "تنبيه أمني", [
        'تم تغيير كلمة مرور حسابك السحابي في Lockvyn.',
        'إذا أجريت التغيير، لا تحتاج اتخاذ إجراء. الباسورد الرئيسي لخزنتك المحلية لم يتغير بسبب هذا الإجراء.',
        'إذا لم تُجرِ التغيير، راجع أمان حسابك فورًا وابدأ استعادة الوصول من مسار الاستعادة الموثوق.'
    ], False, True),
    ("email-address-changed", "email-changed", "تنبيه Lockvyn: تغيّر بريد حسابك", "تم تغيير بريد الحساب", "تنبيه أمني", [
        'تم تغيير البريد من <span dir="ltr">{{ .OldEmail }}</span> إلى <span dir="ltr">{{ .Email }}</span>.',
        'استخدم البريد الجديد عند تسجيل الدخول إلى حساب السحابة.',
        'إذا لم تُجرِ التغيير، راجع أمان حسابك فورًا. هذه الرسالة إشعار وليست طلب تأكيد جديدًا.'
    ], False, True),
    ("mfa-method-added", "mfa-added", "تنبيه Lockvyn: أُضيفت وسيلة 2FA", "أُضيفت وسيلة تحقق إضافية", "حماية الحساب", [
        'أُضيفت وسيلة تحقق من نوع <span dir="ltr">{{ .FactorType }}</span> إلى حسابك السحابي.',
        'إذا أكملت إعدادها، احفظ نسخة آمنة من إعداد تطبيق المصادقة. كود الدخول يُؤخذ من تطبيقك ولا يُرسل في هذه الرسالة.',
        'إذا لم تضف هذه الوسيلة، راجع أمان حسابك فورًا.'
    ], False, True),
    ("mfa-method-removed", "mfa-removed", "تنبيه Lockvyn: أُزيلت وسيلة 2FA", "أُزيلت وسيلة تحقق إضافية", "تنبيه أمني", [
        'أُزيلت وسيلة تحقق من نوع <span dir="ltr">{{ .FactorType }}</span> من حسابك السحابي.',
        'إذا كانت آخر وسيلة تحقق، فلن يبقى حساب السحابة محميًا بعامل إضافي. يمكنك إضافة وسيلة موثوقة من إعدادات 2FA في Lockvyn.',
        'إذا لم تُزلها، راجع أمان حسابك فورًا. هذه الرسالة لا تغيّر تشفير الخزنة المحلية.'
    ], False, True),
    ("sign-in-method-linked", "identity-linked", "تنبيه Lockvyn: رُبطت طريقة دخول", "رُبطت طريقة دخول بالحساب", "تنبيه أمني", [
        'رُبطت طريقة الدخول <span dir="ltr">{{ .Provider }}</span> بحسابك السحابي.',
        'إذا طلبت الربط، هذه رسالة لتأكيد حدوثه. إذا لم تطلبه، راجع طرق الدخول وأمان حسابك فورًا.'
    ], False, False),
    ("sign-in-method-removed", "identity-unlinked", "تنبيه Lockvyn: أُزيلت طريقة دخول", "أُزيلت طريقة دخول من الحساب", "تنبيه أمني", [
        'أُزيلت طريقة الدخول <span dir="ltr">{{ .Provider }}</span> من حسابك السحابي.',
        'تأكد أن لديك طريقة دخول أخرى قبل الاعتماد على هذا الحساب. إذا لم تطلب الإزالة، راجع أمان حسابك فورًا.'
    ], False, False),
    ("phone-number-changed", "phone-changed", "تنبيه Lockvyn: تغيّر رقم الحساب", "تم تغيير رقم الهاتف", "تنبيه أمني", [
        'تم تغيير الرقم من <span dir="ltr">{{ .OldPhone }}</span> إلى <span dir="ltr">{{ .Phone }}</span>.',
        'إذا لم تطلب التغيير، راجع أمان حسابك فورًا. هذا القالب يُستخدم فقط عند تفعيل ربط الهاتف بالحساب.'
    ], False, False),
]


def generate():
    OUT.mkdir(parents=True, exist_ok=True)
    manifest = []
    for route, filename, subject, heading, category, paragraphs, token, enabled in CATALOG:
        code = '''<p style="margin:20px 0 6px;color:#646070">رمزك لمرة واحدة</p>
<p dir="ltr" style="margin:0 0 20px;font:32px/1.5 monospace;text-align:center;letter-spacing:5px;background:#f3f0ff;color:#55429e;padding:16px;border-radius:10px">{{ .Token }}</p>
<p style="color:#646070">الرمز صالح لفترة محدودة ويُستخدم مرة واحدة. لا تشاركه مع أي شخص.</p>''' if token else ''
        body = '\n'.join(f'<p style="margin:14px 0">{p}</p>' for p in paragraphs)
        if token:
            body = f'<p style="margin:14px 0">{paragraphs[0]}</p>\n{code}\n' + '\n'.join(f'<p style="margin:14px 0">{p}</p>' for p in paragraphs[1:])
        document = f'''<!doctype html>
<html lang="ar" dir="rtl">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{html.escape(subject)}</title></head>
<body style="margin:0;background:#f8f7fd;font:16px/1.8 Arial,sans-serif;color:#242335">
<div style="display:none;max-height:0;overflow:hidden">{html.escape(heading)} — حساب Lockvyn السحابي.</div>
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:24px 12px">
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;background:#fff;border:1px solid #eceaf4;border-radius:16px"><tr><td style="padding:28px;text-align:right">
<p dir="ltr" style="margin:0;color:#6553c4;font-size:28px;font-weight:bold;text-align:right">Lockvyn</p>
<p style="margin:8px 0;color:#646070;font-size:13px">{html.escape(category)}</p>
<h1 style="font-size:24px;margin:20px 0 12px">{html.escape(heading)}</h1>
{body}
<p style="border-top:1px solid #eceaf4;padding-top:18px;margin-top:24px;color:#646070;font-size:14px">هذه الرسالة تخص حساب السحابة. لا تحتوي على كلمات المرور المحفوظة، ولا تستعيد الباسورد الرئيسي للخزنة المحلية.</p>
</td></tr></table></td></tr></table>
</body></html>
'''
        OUT.joinpath(filename + '.html').write_text(document, encoding='utf-8')
        manifest.append({'route': route, 'file': filename + '.html', 'subject': subject, 'notification_enabled': enabled})
    OUT.joinpath('catalog.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Generated {len(manifest)} Lockvyn email templates.')


if __name__ == '__main__':
    generate()
