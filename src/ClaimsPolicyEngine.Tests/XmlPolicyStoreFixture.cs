﻿using NUnit.Framework;

namespace ClaimsPolicyEngine.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ClaimsPolicyEngine;
    using Exceptions;
    using Model;
    using Mocks;
	
    public class XmlPolicyStoreFixture
    {
        [Test]
        public void AddPolicyRuleShouldPassIfExistingScope()
        {
            XmlPolicyStore store = new XmlPolicyStore("My Xml Store Path", new MockXmlRepository(@"..\..\content\claimMappings-PassingTest2.xml"));

            int initialScopeCount = store.RetrieveScopes().Count();

            IList<InputPolicyClaim> inputClaims = new List<InputPolicyClaim>();
            Issuer issuer = new Issuer("http://myIssuer1");
            ClaimType claimType = new ClaimType("http://myClaimType", "myClaimType");
            inputClaims.Add(new InputPolicyClaim(issuer, claimType, "nicolas"));
            PolicyRule newRule = new PolicyRule(AssertionsMatch.Any, inputClaims, new OutputPolicyClaim(claimType, string.Empty, CopyFromConstants.InputValue));

            store.AddPolicyRule(new Uri("http://localhost/1"), newRule);

            int expectedScopeCount = initialScopeCount;
            Assert.AreEqual(expectedScopeCount, store.RetrieveScopes().Count());
            Assert.AreEqual(2, store.RetrieveScopes().ElementAt(0).Rules.Count);
        }

        [Test]
        public void RemovePolicyRuleShouldPassIfExistingScope()
        {
            XmlPolicyStore store = new XmlPolicyStore("My Xml Store Path", new MockXmlRepository(@"..\..\content\claimMappings-PassingTest4.xml"));

            Assert.AreEqual(1, store.RetrieveScopes().ElementAt(0).Rules.Count);

            var rule = store.RetrieveScopes().ElementAt(0).Rules.ElementAt(0);

            store.RemovePolicyRule(new Uri("http://localhost/1"), rule);

            Assert.AreEqual(0, store.RetrieveScopes().ElementAt(0).Rules.Count);
        }

        [Test]
        public void RemoveIssuerShouldPass()
        {
            XmlPolicyStore store = new XmlPolicyStore("My Xml Store Path", new MockXmlRepository(@"..\..\content\claimMappings-PassingTest4.xml"));

            store.RemoveIssuer(new Uri("http://localhost/1"), new Issuer("http://myIssuer21"));

            Assert.AreEqual(1, store.RetrieveScopes().ElementAt(0).Issuers.Count);
        }

        [Test]
        public void RemoveIssuerShouldThrowIsThereAreRulesForIssuer()
        {
            XmlPolicyStore store = new XmlPolicyStore("My Xml Store Path", new MockXmlRepository(@"..\..\content\claimMappings-FailingTest12.xml"));

            Assert.Throws<PolicyScopeException>(()=>store.RemoveIssuer(new Uri("http://localhost/1"), new Issuer("http://myIssuer1")));
        }

        [Test]
        public void AddPolicyRuleShouldThrowIfNotExistingScope()
        {
            XmlPolicyStore store = new XmlPolicyStore("My Xml Store Path", new MockXmlRepository(@"..\..\content\claimMappings-PassingTest2.xml"));
            
            IList<InputPolicyClaim> inputClaims = new List<InputPolicyClaim>();
            Issuer issuer = new Issuer("http://myIssuer1");
            ClaimType claimType = new ClaimType("http://myClaimType", "myClaimType");
            inputClaims.Add(new InputPolicyClaim(issuer, claimType, "nicolas"));
            PolicyRule newRule = new PolicyRule(AssertionsMatch.Any, inputClaims, new OutputPolicyClaim(claimType, string.Empty, CopyFromConstants.InputValue));

            Assert.Throws<PolicyScopeException>(()=>store.AddPolicyRule(new Uri("http://notExistingScope/1"), newRule));
        }

        [Test]
        public void AddPolicyRuleShouldAddNewOutputClaimTypeIfDoesNotExists()
        {
            XmlPolicyStore store = new XmlPolicyStore("My Xml Store Path", new MockXmlRepository(@"..\..\content\claimMappings-PassingTest3.xml"));
            var scopeUri = new Uri("http://localhost/1");

            IList<InputPolicyClaim> inputClaims = new List<InputPolicyClaim>();
            Issuer issuer = new Issuer("http://myIssuer1", "6f7051ece706096ac5a05ecb1860e2151c11b491", "myIssuer1");           
            ClaimType claimType = new ClaimType("http://myClaimType", "myClaimType");

            inputClaims.Add(new InputPolicyClaim(issuer, claimType, "nicolas"));

            ClaimType newClaimType = new ClaimType("http://newClaimType", "myNewClaimType");

            PolicyRule newRule = new PolicyRule(AssertionsMatch.Any, inputClaims, new OutputPolicyClaim(newClaimType, string.Empty, CopyFromConstants.InputValue));

            store.AddPolicyRule(scopeUri, newRule);
            var scope = store.RetrieveScope(scopeUri);

            Assert.AreEqual(2, scope.ClaimTypes.Count);
            Assert.AreEqual(newClaimType.FullName, scope.ClaimTypes.ElementAt(1).FullName);
            Assert.AreEqual(newClaimType.DisplayName, scope.ClaimTypes.ElementAt(1).DisplayName);
        }

        [Test]
        public void AddPolicyRuleShouldAddNewInputClaimTypeIfDoesNotExists()
        {
            XmlPolicyStore store = new XmlPolicyStore("My Xml Store Path", new MockXmlRepository(@"..\..\content\claimMappings-PassingTest3.xml"));
            var scopeUri = new Uri("http://localhost/1");

            IList<InputPolicyClaim> inputClaims = new List<InputPolicyClaim>();
            Issuer issuer = new Issuer("http://myIssuer1", "6f7051ece706096ac5a05ecb1860e2151c11b491", "myIssuer1");

            ClaimType claimType = new ClaimType("http://myClaimType", "myClaimType");
            ClaimType newClaimType = new ClaimType("http://newClaimType", "myNewClaimType");

            inputClaims.Add(new InputPolicyClaim(issuer, newClaimType, "nicolas"));

            PolicyRule newRule = new PolicyRule(AssertionsMatch.Any, inputClaims, new OutputPolicyClaim(claimType, string.Empty, CopyFromConstants.InputValue));

            store.AddPolicyRule(scopeUri, newRule);
            var scope = store.RetrieveScope(scopeUri);

            Assert.AreEqual(2, scope.ClaimTypes.Count);
            Assert.AreEqual(newClaimType.FullName, scope.ClaimTypes.ElementAt(1).FullName);
            Assert.AreEqual(newClaimType.DisplayName, scope.ClaimTypes.ElementAt(1).DisplayName);
        }

        [Test]
        public void PolicyStoreCtorShouldThrowIfInvalidStoreName()
        {
            bool exceptionThrown = false;
            try
            {
                new XmlPolicyStore(string.Empty, new FileXmlRepository());
            }
            catch (ArgumentException)
            {
                exceptionThrown = true;
            }

            Assert.IsTrue(exceptionThrown);

            exceptionThrown = false;
            try
            {
                new XmlPolicyStore(null, new FileXmlRepository());
            }
            catch (ArgumentException)
            {
                exceptionThrown = true;
            }

            Assert.IsTrue(exceptionThrown);
        }

        [Test]
        public void RetrieveScopesShouldThrowIfInvalidInputClaimType()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest1.xml", new FileXmlRepository());

            Assert.Throws<PolicyClaimException>(()=>store.RetrieveScopes());
        }

        [Test]
        public void RetrieveScopesShouldThrowIfInvalidOutputClaimType()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest2.xml", new FileXmlRepository());

            Assert.Throws<PolicyClaimException>(()=>store.RetrieveScopes());
        }

        [Test]
        public void RetrieveScopesShouldThrowIfInvalidInputIssuer()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest3.xml", new FileXmlRepository());

            Assert.Throws<PolicyClaimException>(()=>store.RetrieveScopes());
        }

        [Test]
        public void RetrieveScopesShouldThrowIfWildcardOnOutputClaim()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest4.xml", new FileXmlRepository());

            Assert.Throws<PolicyClaimException>(()=>store.RetrieveScopes());
        }

        [Test]
        public void RetrieveScopesShouldThrowIfCopyFromInputWithMultipleInputClaims()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest5.xml", new FileXmlRepository());

            Assert.Throws<PolicyRuleException>(()=>store.RetrieveScopes());
        }

        [Test]
        public void RetrieveScopesShouldThrowIfCopyFromInputWithOutputValue()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest6.xml", new FileXmlRepository());

            Assert.Throws<PolicyClaimException>(()=> store.RetrieveScopes());
        }

        [Test]
        public void ValueOnOutputClaimShouldBePresentIfCopyFromInputIsFalseOrAbsent()
        {
            bool exceptionThrown = false;

            try
            {
                XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest10.xml", new FileXmlRepository());
                store.RetrieveScopes();
            }
            catch (PolicyClaimException)
            {
                exceptionThrown = true;
            }

            Assert.IsTrue(exceptionThrown);

            exceptionThrown = false;
            try
            {
                XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest11.xml", new FileXmlRepository());
                store.RetrieveScopes();
            }
            catch (PolicyClaimException)
            {
                exceptionThrown = true;
            }

            Assert.IsTrue(exceptionThrown);
        }

        [Test]
        public void RetrieveScopesShouldThrowIfDuplicatedUrisScope()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest7.xml", new FileXmlRepository());
            Assert.Throws<PolicyScopeException>(()=> store.RetrieveScopes());
        }

        [Test]
        public void RetrieveScopesShouldThrowIfClaimTypeDeclaredOnDifferentScope()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest8.xml", new FileXmlRepository());
            Assert.Throws<PolicyClaimException>(()=> store.RetrieveScopes());
        }

        [Test]
        public void RetrieveScopesShouldThrowIfIssuerDeclaredOnDifferentScope()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-FailingTest9.xml", new FileXmlRepository());
            Assert.Throws<PolicyClaimException>(()=> store.RetrieveScopes());
        }

        [Test]
        public void ShouldRetrieveClaimsPolicies()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-PassingTest1.xml", new FileXmlRepository());
            
            IEnumerable<PolicyScope> policyScopes = store.RetrieveScopes();

            Assert.IsNotNull(policyScopes);
            Assert.AreEqual(2, policyScopes.Count());

            Assert.AreEqual(new Uri("http://localhost/1"), policyScopes.ElementAt(0).Uri);
            Assert.AreEqual(2, policyScopes.ElementAt(0).Rules.Count());
            Assert.AreEqual(AssertionsMatch.All, policyScopes.ElementAt(0).Rules.ElementAt(0).AssertionsMatch);
            Assert.AreEqual(1, policyScopes.ElementAt(0).Rules.ElementAt(0).InputClaims.Count());
            Assert.AreEqual("http://myInputClaimType1", policyScopes.ElementAt(0).Rules.ElementAt(0).InputClaims.ElementAt(0).ClaimType.FullName);
            Assert.AreEqual("http://myIssuer1", policyScopes.ElementAt(0).Rules.ElementAt(0).InputClaims.ElementAt(0).Issuer.Uri);
            Assert.AreEqual("*", policyScopes.ElementAt(0).Rules.ElementAt(0).InputClaims.ElementAt(0).Value);
            Assert.AreEqual("http://myOutputClaimType1", policyScopes.ElementAt(0).Rules.ElementAt(0).OutputClaim.ClaimType.FullName);
            Assert.AreEqual("myOutputClaim", policyScopes.ElementAt(0).Rules.ElementAt(0).OutputClaim.Value);
            Assert.IsFalse(policyScopes.ElementAt(0).Rules.ElementAt(0).OutputClaim.CopyFromInput);
            
            Assert.AreEqual(AssertionsMatch.Any, policyScopes.ElementAt(0).Rules.ElementAt(1).AssertionsMatch);
            Assert.AreEqual(1, policyScopes.ElementAt(0).Rules.ElementAt(1).InputClaims.Count());
            Assert.AreEqual("http://myInputClaimType1", policyScopes.ElementAt(0).Rules.ElementAt(1).InputClaims.ElementAt(0).ClaimType.FullName);
            Assert.AreEqual("http://myIssuer2", policyScopes.ElementAt(0).Rules.ElementAt(1).InputClaims.ElementAt(0).Issuer.Uri);
            Assert.AreEqual("inputClaimValue", policyScopes.ElementAt(0).Rules.ElementAt(1).InputClaims.ElementAt(0).Value);
            Assert.AreEqual("http://myOutputClaimType1", policyScopes.ElementAt(0).Rules.ElementAt(1).OutputClaim.ClaimType.FullName);
            Assert.AreEqual(string.Empty, policyScopes.ElementAt(0).Rules.ElementAt(1).OutputClaim.Value);
            Assert.IsTrue(policyScopes.ElementAt(0).Rules.ElementAt(1).OutputClaim.CopyFromInput);

            Assert.AreEqual(new Uri("http://localhost/2"), policyScopes.ElementAt(1).Uri);
            Assert.AreEqual(1, policyScopes.ElementAt(1).Rules.Count());
            Assert.AreEqual(AssertionsMatch.All, policyScopes.ElementAt(1).Rules.ElementAt(0).AssertionsMatch);
            Assert.AreEqual(2, policyScopes.ElementAt(1).Rules.ElementAt(0).InputClaims.Count());
            Assert.AreEqual("http://myInputClaimType2", policyScopes.ElementAt(1).Rules.ElementAt(0).InputClaims.ElementAt(0).ClaimType.FullName);
            Assert.AreEqual("http://myIssuer3", policyScopes.ElementAt(1).Rules.ElementAt(0).InputClaims.ElementAt(0).Issuer.Uri);
            Assert.AreEqual("scope 2 - input claim value from myIssuer3", policyScopes.ElementAt(1).Rules.ElementAt(0).InputClaims.ElementAt(0).Value);
            Assert.AreEqual("http://myInputClaimType2", policyScopes.ElementAt(1).Rules.ElementAt(0).InputClaims.ElementAt(1).ClaimType.FullName);
            Assert.AreEqual("http://myIssuer4", policyScopes.ElementAt(1).Rules.ElementAt(0).InputClaims.ElementAt(1).Issuer.Uri);
            Assert.AreEqual("scope 2 - input claim value from myIssuer4", policyScopes.ElementAt(1).Rules.ElementAt(0).InputClaims.ElementAt(1).Value);
            Assert.AreEqual("http://myOutputClaimType2", policyScopes.ElementAt(1).Rules.ElementAt(0).OutputClaim.ClaimType.FullName);
            Assert.AreEqual("scope 2 - output claim value", policyScopes.ElementAt(1).Rules.ElementAt(0).OutputClaim.Value);
            Assert.IsFalse(policyScopes.ElementAt(1).Rules.ElementAt(0).OutputClaim.CopyFromInput);
        }

        [Test]
        public void ShouldRetrieveExistingIssuerByDisplayName()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-PassingTest1.xml", new FileXmlRepository());

            var issuer = store.RetrieveIssuer(new Uri("http://localhost/1"), "myIssuer1");

            Assert.IsNotNull(issuer);
            Assert.AreEqual("myIssuer1", issuer.DisplayName);
        }

        [Test]
        public void ShouldRetrieveNullIfIssuerDoesNotExists()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-PassingTest1.xml", new FileXmlRepository());

            var issuer = store.RetrieveIssuer(new Uri("http://localhost/1"), "Inexisting Issuer");

            Assert.IsNull(issuer);
        }

        [Test]
        public void ShouldSearchForIssuerUsingCaseInsensitiveName()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-PassingTest1.xml", new FileXmlRepository());

            var issuer = store.RetrieveIssuer(new Uri("http://localhost/1"), "myissuer1");
            Assert.IsNotNull(issuer);
            Assert.AreEqual("myIssuer1", issuer.DisplayName);

            issuer = store.RetrieveIssuer(new Uri("http://localhost/1"), "MYISSUER1");
            Assert.IsNotNull(issuer);
            Assert.AreEqual("myIssuer1", issuer.DisplayName);
        }

        [Test]
        public void ShouldNotRetrieveIssuersFromOtherScopes()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-PassingTest1.xml", new FileXmlRepository());

            var issuer = store.RetrieveIssuer(new Uri("http://localhost/1"), "myIssuer3");

            Assert.IsNull(issuer);
        }

        [Test]
        public void ShouldThrowIfScopeNotFoundOnIssuerRetrieval()
        {
            XmlPolicyStore store = new XmlPolicyStore(TestContext.CurrentContext.TestDirectory + @"..\..\..\content\claimMappings-PassingTest1.xml", new FileXmlRepository());

            Assert.Throws<PolicyScopeException>(()=> store.RetrieveIssuer(new Uri("http://inexistentScope/"), "myIssuer1"));
        }
    }
}
