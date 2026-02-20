# Configuration Checklist

Use this checklist to ensure your Document Translation application is properly configured.

## Pre-Deployment Checklist

### Azure Resources ?

#### Storage Account
- [ ] Storage account created
- [ ] Account name noted: `_______________________`
- [ ] Container `translations` created
- [ ] Managed Identity enabled
- [ ] Role: Storage Blob Data Contributor assigned to App Registration
- [ ] Role: Storage Blob Data Contributor assigned to Translation Service MI
- [ ] Firewall rules configured (if needed)
- [ ] Network access verified

#### Azure Document Translation Service
- [ ] Translation service created
- [ ] Endpoint URL noted: `_______________________`
- [ ] Region noted: `_______________________`
- [ ] Managed Identity enabled
- [ ] Storage access granted to Managed Identity
- [ ] Service tier confirmed (S1 recommended)

#### Azure AD App Registration
- [ ] App Registration created
- [ ] Client ID noted: `_______________________`
- [ ] Tenant ID noted: `_______________________`
- [ ] Client Secret created and noted: `_______________________`
- [ ] Secret expiration date: `_______________________`
- [ ] API Permissions granted
- [ ] Storage Blob Data Contributor role assigned

#### Application Insights (Optional)
- [ ] Application Insights resource created
- [ ] Connection string noted: `_______________________`
- [ ] Log retention configured
- [ ] Alerts configured

### Application Configuration ?

#### User Secrets (Development)
```bash
- [ ] dotnet user-secrets init
- [ ] ApplicationInsights:ConnectionString set
- [ ] AzureTranslation:Endpoint set
- [ ] AzureTranslation:Region set
- [ ] AzureBlobStorage:AccountName set
- [ ] AzureBlobStorage:TenantId set
- [ ] AzureBlobStorage:ClientId set
- [ ] AzureBlobStorage:ClientSecret set
- [ ] AzureBlobStorage:ContainerName set
```

#### appsettings.json (Production)
- [ ] Configuration section present
- [ ] No sensitive data in source control
- [ ] Environment-specific settings configured

#### Azure App Service Configuration (Production)
- [ ] All app settings configured in portal
- [ ] Connection strings configured
- [ ] Managed Identity enabled for App Service
- [ ] Custom domains configured (if applicable)
- [ ] SSL certificate configured

### Security Configuration ?

#### Authentication & Authorization
- [ ] App Registration client secret is secure
- [ ] Client secret stored in Key Vault (production)
- [ ] Managed Identity configured correctly
- [ ] Role assignments verified
- [ ] Least-privilege access implemented

#### Network Security
- [ ] Storage firewall rules configured
- [ ] Virtual network integration (if needed)
- [ ] Private endpoints configured (if needed)
- [ ] DDoS protection enabled (production)

#### Data Protection
- [ ] HTTPS enforced
- [ ] HSTS configured
- [ ] Sensitive data not logged
- [ ] File upload size limits configured

### Application Settings ?

#### File Upload Configuration
```csharp
- [ ] MaxRequestBodySize: 524288000 (500 MB)
- [ ] MultipartBodyLengthLimit: 524288000
- [ ] Request timeout configured
```

#### Supported File Types
- [ ] PDF (.pdf) ?
- [ ] Word (.docx, .doc) ?
- [ ] PowerPoint (.pptx, .ppt) ?
- [ ] Excel (.xlsx, .xls) ?
- [ ] Text (.txt) ?
- [ ] HTML (.html, .htm) ?
- [ ] RTF (.rtf) ?
- [ ] OpenDocument (.odt, .ods, .odp) ?

### Testing Checklist ?

#### Pre-Launch Testing
- [ ] Build completes successfully
- [ ] Application starts without errors
- [ ] Translation page loads
- [ ] File upload works
- [ ] Single file translation (sync)
- [ ] Single file translation (async)
- [ ] Multi-file translation
- [ ] Multi-language translation
- [ ] Image extraction (PDF)
- [ ] Image extraction (Word)
- [ ] Download functionality
- [ ] Cleanup functionality
- [ ] Error handling
- [ ] Application Insights receiving data

#### Performance Testing
- [ ] Large file upload (> 50 MB)
- [ ] Long-running translation (> 5 minutes)
- [ ] Concurrent users
- [ ] Memory usage acceptable
- [ ] No memory leaks

#### Security Testing
- [ ] Authentication works
- [ ] Authorization verified
- [ ] No secrets in client-side code
- [ ] File type validation works
- [ ] File size validation works
- [ ] XSS prevention verified
- [ ] CSRF protection enabled

### Monitoring & Logging ?

#### Application Insights
- [ ] Connection string configured
- [ ] Telemetry flowing
- [ ] Custom events tracked
- [ ] Exceptions logged
- [ ] Performance counters tracked
- [ ] Dependency tracking enabled

#### Azure Monitor
- [ ] Resource health checks configured
- [ ] Alert rules created
- [ ] Action groups defined
- [ ] Log Analytics workspace linked

#### Alerts (Recommended)
- [ ] High error rate alert
- [ ] Storage capacity alert
- [ ] Translation service quota alert
- [ ] Application performance alert
- [ ] Failed authentication alert

### Documentation ?

- [ ] README.md reviewed
- [ ] QUICKSTART.md available
- [ ] AZURE_SETUP.md complete
- [ ] TESTING_GUIDE.md available
- [ ] API documentation (if applicable)
- [ ] User guide available
- [ ] Admin guide available

### Deployment Checklist ?

#### Pre-Deployment
- [ ] Code peer-reviewed
- [ ] All tests passing
- [ ] Dependencies updated
- [ ] Security scan completed
- [ ] Performance benchmarks met
- [ ] Documentation updated

#### Deployment Steps
- [ ] Backup current configuration
- [ ] Deploy to staging first
- [ ] Smoke tests in staging
- [ ] Deploy to production
- [ ] Verify deployment
- [ ] Monitor for errors

#### Post-Deployment
- [ ] Application accessible
- [ ] Health check passing
- [ ] Test translation works
- [ ] Monitor logs for errors
- [ ] Verify Application Insights data
- [ ] Update documentation
- [ ] Notify stakeholders

### Maintenance Checklist ?

#### Daily
- [ ] Check Application Insights for errors
- [ ] Monitor storage usage
- [ ] Review failed translations

#### Weekly
- [ ] Review performance metrics
- [ ] Check cost analysis
- [ ] Review security alerts
- [ ] Clean up old blob files

#### Monthly
- [ ] Rotate client secrets (if expiring)
- [ ] Review and update dependencies
- [ ] Review access permissions
- [ ] Optimize storage costs
- [ ] Review Application Insights retention
- [ ] Update documentation

### Troubleshooting Quick Reference ?

#### Common Issues & Solutions

**Issue: Storage Access Denied**
- [ ] Verify role assignments (wait 5-10 minutes after creating)
- [ ] Check client ID, tenant ID, secret
- [ ] Verify managed identity enabled
- [ ] Check firewall rules

**Issue: Translation Service Error**
- [ ] Verify endpoint URL format
- [ ] Check region setting
- [ ] Confirm managed identity access
- [ ] Verify service quota

**Issue: File Upload Fails**
- [ ] Check file size limits
- [ ] Verify file type support
- [ ] Check network timeout settings
- [ ] Review Kestrel configuration

**Issue: Application Insights Not Receiving Data**
- [ ] Verify connection string
- [ ] Check instrumentation key
- [ ] Wait 2-5 minutes for data
- [ ] Check firewall/network rules

### Production Readiness Checklist ?

#### Infrastructure
- [ ] High availability configured
- [ ] Auto-scaling rules defined
- [ ] Backup and recovery plan
- [ ] Disaster recovery tested
- [ ] Load balancing configured
- [ ] CDN configured (if needed)

#### Security
- [ ] Key Vault integrated
- [ ] Secrets rotation policy
- [ ] Security audit completed
- [ ] Penetration test completed
- [ ] Compliance requirements met
- [ ] Data encryption verified

#### Operations
- [ ] Monitoring dashboard created
- [ ] On-call procedures documented
- [ ] Incident response plan
- [ ] SLA defined
- [ ] Support contact info
- [ ] Escalation procedures

#### Performance
- [ ] Load testing completed
- [ ] Performance baseline established
- [ ] Optimization opportunities identified
- [ ] Caching strategy implemented
- [ ] Database optimization (if applicable)

### Sign-off ?

**Configured by:** _______________________  
**Date:** _______________________  
**Environment:** [ ] Development [ ] Staging [ ] Production  
**Approved by:** _______________________  
**Date:** _______________________  

### Notes / Issues

```
_____________________________________________________________________________
_____________________________________________________________________________
_____________________________________________________________________________
_____________________________________________________________________________
```

---

**Print this checklist and use it for each environment deployment!**
