package main

import (
 "bytes"
 "encoding/json"
 "fmt"
 "os"
 "path/filepath"
 "strings"
 "text/template"
)

type row struct { Route string `json:"route"`; File string `json:"file"`; Subject string `json:"subject"` }
func must(ok bool, why string) { if !ok { panic(why) } }
func main() {
 raw, err := os.ReadFile("supabase/templates/catalog.json"); if err != nil { panic(err) }
 var rows []row; if err=json.Unmarshal(raw,&rows);err!=nil {panic(err)}
 must(len(rows)==13,"Expected 13 Auth templates")
 vars:=map[string]string{"Token":"123456","Email":"synthetic@example.com","NewEmail":"new@example.com","OldEmail":"old@example.com","Phone":"+15555550101","OldPhone":"+15555550100","Provider":"synthetic-provider","FactorType":"totp"}
 subjects:=map[string]bool{}
 for _,r:=range rows {
  must(strings.Contains(r.Subject,"Lockvyn")&&!strings.Contains(r.Subject,"{{"),r.Route+": clear branded subject without tokens")
  must(!subjects[r.Subject],"Subjects must describe separate cases"); subjects[r.Subject]=true
  raw,err=os.ReadFile(filepath.Join("supabase/templates",r.File));if err!=nil{panic(err)}
  body:=string(raw);must(strings.Contains(body,`lang="ar" dir="rtl"`),r.Route+": Arabic direction")
  must(strings.Count(body,"<h1")==1&&strings.Count(body,"<!doctype html>")==1,r.Route+": one complete email")
  must(!strings.Contains(body,".Data")&&!strings.Contains(body,".SiteURL")&&!strings.Contains(body,".ConfirmationURL"),r.Route+": no untrusted metadata or unsupported native redirect")
  t,err:=template.New(r.Route).Option("missingkey=error").Parse(body);if err!=nil{panic(err)}
  var out bytes.Buffer;if err=t.Execute(&out,vars);err!=nil{panic(err)}
  must(!strings.Contains(out.String(),"{{")&&!strings.Contains(out.String(),"<no value>"),r.Route+": all variables render")
  must(strings.Contains(out.String(),"الخزنة المحلية"),r.Route+": vault/account distinction")
  fmt.Println("PASS:",r.Route,"Go template rendered")
 }
 fmt.Println("RESULT: 13 email templates validated with synthetic values.")
}
