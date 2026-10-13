// Pure authorization decisions shared by the live Edge Function and CI tests.
// The caller MUST first validate the JWT with Supabase Auth getUser(token).
export function verifiedUserGate(user, claims) {
  if (!user?.id || !user.email_confirmed_at ||
      !claims || claims.sub !== user.id || claims.role !== "authenticated") {
    return { status: 401, body: { error: "Unauthorized" } };
  }
  if (claims.aal !== "aal2") {
    return {
      status: 403,
      body: { error: "Admin access requires verified 2FA", mfaRequired: true },
    };
  }
  return null;
}

export function adminMembershipGate(member, userId) {
  return Boolean(userId && member?.user_id === userId);
}
