using System.Net;
using System.Text.Json;
using BoshaVault.Core;

public static class CloudMfaTests
{
    public static string Token(Guid owner,string aal) => "eyJhbGciOiJIUzI1NiJ9." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new {sub=owner,aal})).TrimEnd('=').Replace('+','-').Replace('/','_') + ".synthetic-signature";
    private sealed class Handler : HttpMessageHandler
    {
        public Guid Owner=Guid.NewGuid(),Factor=Guid.NewGuid(),Challenge=Guid.NewGuid();
        public string Aal="aal1",Code="",LastBody="",Secret="JBSWY3DPEHPK3PXP",Type="totp";
        public bool Verified,WrongOwner,Duplicate,PrivateError,NoUpgrade;
        public readonly List<string> Paths=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            string path=request.RequestUri!.PathAndQuery;Paths.Add(request.Method+" "+path);
            LastBody=request.Content==null?"":await request.Content.ReadAsStringAsync(ct);
            if(request.RequestUri.Scheme!="https"||request.Headers.GetValues("apikey").Single()!=CloudProject.PublishableKey)throw new Exception("Unsafe request");
            if(path.Contains("factors")||path.EndsWith("/user"))if(request.Headers.Authorization?.Scheme!="Bearer")throw new Exception("Missing bearer");
            Guid id=WrongOwner?Guid.NewGuid():Owner;object body;
            if(path.EndsWith("/user"))
            {
                object factor=new {id=Factor,factor_type=Type,friendly_name="Synthetic authenticator",status=Verified?"verified":"unverified"};
                body=new {id,factors=Duplicate?new[]{factor,factor}:new[]{factor}};
            }
            else if(path.EndsWith("/factors"))body=new{id=Factor,type="totp",totp=new{secret=Secret,qr_code="<svg>"+new string('x',90000)+"</svg>",uri="https://untrusted.invalid/secret"}};
            else if(path.EndsWith("/challenge"))body=new{id=Challenge};
            else if(path.EndsWith("/verify")&&path.Contains("factors"))
            {
                using var j=JsonDocument.Parse(LastBody);Code=j.RootElement.GetProperty("code").GetString()!;
                if(j.RootElement.GetProperty("challenge_id").GetGuid()!=Challenge)throw new Exception("Wrong challenge binding");
                body=new{user=new{id},access_token=Token(id,NoUpgrade?"aal1":"aal2"),refresh_token="rotated-synthetic-refresh-token"};
            }
            else body=new{user=new{id},access_token=Token(id,Aal),refresh_token="synthetic-refresh-token-0123456789"};
            return new HttpResponseMessage(PrivateError?HttpStatusCode.BadRequest:HttpStatusCode.OK){Content=new StringContent(PrivateError?"{\"error_code\":\"mfa_verification_failed\",\"message\":\"private-secret-never-echo\"}":JsonSerializer.Serialize(body))};
        }
    }
    public static async Task Run()
    {
        int passed=0;void Check(bool b,string name){if(!b)throw new Exception("FAIL "+name);Console.WriteLine("PASS "+name);passed++;}
        async Task Reject(Func<Task> run,string name){try{await run();throw new Exception("Expected rejection: "+name);}catch(VaultException ex){Check(!ex.Message.Contains("private-secret"),name);}}
        var h=new Handler();using var http=new HttpClient(h);var auth=new SupabaseCloudAccount(http);var s=await auth.SignIn("fake@example.invalid","synthetic password");
        Check(!s.HasSecondFactor,"password session stays at aal1");
        Check(!(await auth.ListFactors(s)).RequiresChallenge(s),"unverified enrollment does not count as 2FA");
        h.Verified=true;var factors=await auth.ListFactors(s);Check(factors.RequiresChallenge(s)&&factors.Totp.Count==1,"verified TOTP requires step-up before cloud use");
        h.Type="phone";factors=await auth.ListFactors(s);Check(factors.RequiresChallenge(s)&&factors.Totp.Count==0,"unsupported verified factor cannot silently bypass MFA");h.Type="totp";
        h.Duplicate=true;await Reject(()=>auth.ListFactors(s),"duplicate factor IDs are refused");h.Duplicate=false;
        h.WrongOwner=true;await Reject(()=>auth.ListFactors(s),"factor list is bound to the signed-in account");h.WrongOwner=false;
        int calls=h.Paths.Count;await Reject(()=>auth.VerifyTotp(s,h.Factor,"١٢٣٤٥٦"),"non-ASCII authenticator code refused before challenge");Check(h.Paths.Count==calls,"invalid code makes no network request");
        var upgraded=await auth.VerifyTotp(s,h.Factor,"123456");Check(upgraded.HasSecondFactor&&upgraded.RefreshToken!=s.RefreshToken&&h.Code=="123456","challenge verification replaces tokens and requires aal2");
        Check(!(await auth.ListFactors(upgraded)).RequiresChallenge(upgraded),"verified session does not prompt unnecessarily");
        h.NoUpgrade=true;await Reject(()=>auth.VerifyTotp(s,h.Factor,"123456"),"verification without assurance upgrade is refused");h.NoUpgrade=false;
        h.WrongOwner=true;await Reject(()=>auth.VerifyTotp(s,h.Factor,"123456"),"verification cannot switch cloud owner");h.WrongOwner=false;
        h.PrivateError=true;await Reject(()=>auth.VerifyTotp(s,h.Factor,"123456"),"MFA errors do not expose server secrets");h.PrivateError=false;
        await Reject(()=>auth.EnrollTotp(s,"fake@example.invalid"),"adding a factor requires existing MFA step-up");
        h.Verified=false;using var enrollment=await auth.EnrollTotp(s,"fake@example.invalid");
        Check(enrollment.Uri.StartsWith("otpauth://totp/")&&enrollment.Uri.Contains("issuer=Lockvyn")&&!enrollment.Uri.Contains("untrusted"),"setup QR URI is assembled locally from validated secret");
        byte[] png=enrollment.RenderQr();Check(png.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"authenticator QR renders locally as a PNG");
        Check(h.Paths.Any(p=>p=="DELETE /auth/v1/factors/"+h.Factor),"unfinished enrollment is removed before retrying setup");
        enrollment.Dispose();Check(enrollment.Secret.Length==0&&enrollment.Uri.Length==0,"setup object releases secret references when closed");
        h.Secret="<bad>";await Reject(()=>auth.EnrollTotp(s,"fake@example.invalid"),"malformed base32 enrollment secret refused");h.Secret="JBSWY3DPEHPK3PXP";
        await auth.VerifyEmail("fake@example.invalid","123456");using(var j=JsonDocument.Parse(h.LastBody))Check(j.RootElement.GetProperty("type").GetString()=="signup"&&!j.RootElement.TryGetProperty("password",out _),"email code confirms signup without connecting or supplying a password");
        await auth.Unenroll(upgraded,h.Factor);Check(h.Paths.Last().StartsWith("DELETE "),"factor removal uses authenticated DELETE");
        Console.WriteLine($"RESULT: {passed} cloud MFA/email-code checks passed.");
    }
}
