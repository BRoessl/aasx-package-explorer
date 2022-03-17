/*
Copyright (c) 2018-2021 Festo AG & Co. KG <https://www.festo.com/net/de_de/Forms/web/contact_international>
Author: Michael Hoffmeister

This source code is licensed under the Apache License 2.0 (see LICENSE.txt).

This source code may use other Open Source software components (see LICENSE.txt).
*/

//clarify copyright topic concerning EKS InTec GmbH input

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AasxIntegrationBase;
using AdminShellNS;
using Aml.Engine.CAEX;
using Aml.Engine.CAEX.Extensions;
using Aml.Engine.AmlObjects;
using Aml.Engine.AmlObjects.Extensions;
using Aml.Engine.Services;

namespace AasxAmlImExport
{
    /// <summary>
    /// This class provides export functionality to AutomationML.
    /// This class makes an approach to translate a Submodel into respective AML roles and attributes
    /// </summary>
    public class AmlExportStruct : AmlExportBase
    {
        public static bool ExportTo(
            AdminShellPackageEnv package,
            string amlfn,
            AdminShell.Submodel submodel)
        {
            if (!IsInputValid()) return false;
            CAEXDocument document = CreateMainDocument();
            RoleClassLibType smRCL = CreateRoleClassLibForSubmodels(document);
            RoleFamilyType smRC = GetInitializedSubmodelRoleClass(submodel, smRCL);
            WriteSubModelElements(smRC, package, submodel.submodelElements);
            document.SaveToFile(amlfn, prettyPrint: true);
            return true;

            bool IsInputValid()
            {
                return package != null && amlfn != null && package.AasEnv != null && submodel != null;
            }
        }

        private static RoleFamilyType GetInitializedSubmodelRoleClass(AdminShellV20.Submodel submodel, RoleClassLibType smRCL)
        {
            RoleFamilyType smRC = smRCL.RoleClass.Append(submodel.idShort);
            smRC.ID = Guid.NewGuid().ToString();
            smRC.Description = submodel.identification.idType.ToString() + "-PATH://" + submodel.identification.id.ToString();
            smRC.RefBaseClassPath = AmlConst.Roles.SubmodelRoleReference;
            return smRC;
        }

        private static RoleClassLibType CreateRoleClassLibForSubmodels(CAEXDocument document)
        {
            return document.CAEXFile.RoleClassLib.Append(AmlConst.Names.RootSubModelRoleClass);
        }

        /// <summary>
        /// (CAEX V3.0)
        /// </summary>
        private static CAEXDocument CreateMainDocument() => CAEXDocument.New_CAEXDocument(CAEXDocument.CAEXSchema.CAEX3_0);

        private static void WriteSubModelElements(
            RoleFamilyType role,
            AdminShellPackageEnv package,
            AdminShell.SubmodelElementWrapperCollection submodels)
        {
            IterateSubmodels(submodels, id => role.Attribute.Append(id), package);
        }

        private static void WriteSubModelElements(
            AttributeType submodelAttribute, 
            AdminShellPackageEnv package, 
            AdminShell.SubmodelElementWrapperCollection submodels)
        {
            IterateSubmodels(submodels, id => submodelAttribute.Attribute.Append(id), package);
        }

        private static void IterateSubmodels(
            AdminShell.SubmodelElementWrapperCollection submodelElements,
            Func<string, AttributeType> getAttributeType,
            AdminShellPackageEnv package)
        {
            submodelElements.ForEach(element =>
            {
                AttributeType smAtt = getAttributeType(element.submodelElement.idShort);
                SwitchSubmodelElement(package, element, smAtt);
                SetMatchingConceptDescription(package, element, smAtt);
            });
        }

        private static void SwitchSubmodelElement(AdminShellPackageEnv package, AdminShellV20.SubmodelElementWrapper sme, AttributeType smAttChild)
        {
            switch (sme.submodelElement)
            {
                case AdminShell.Property smep:
                    if (!string.IsNullOrEmpty(smep.valueType)) smAttChild.AttributeDataType = "xs:" + smep.valueType.Trim();
                    break;
                case AdminShell.SubmodelElementCollection smec:
                    WriteSubModelElements(smAttChild, package, smec.value);
                    break;
                case AdminShell.File smef:
                    //if (smef.mimeType != null)
                    break;
            }
        }

        private static void SetMatchingConceptDescription(
            AdminShellPackageEnv package, 
            AdminShellV20.SubmodelElementWrapper sme, 
            AttributeType attribute)
        {
            AdminShellV20.ConceptDescription description = GetMatchingDescription(package.AasEnv.ConceptDescriptions, sme);
            if (description == null) return;
            if (HasEnglishDescription(description)) SetDescription(attribute, description);
            AddIriAsRefSemanticForAttribute(attribute, description);
        }

        private static void AddIriAsRefSemanticForAttribute(
            AttributeType attribute, 
            AdminShellV20.ConceptDescription decription)
        {
            RefSemanticType refSem = attribute.RefSemantic.Append();

            refSem.CorrespondingAttributePath 
                = decription.identification.idType.ToString() 
                + "-PATH://" 
                + decription.identification.id.ToString();
        }

        private static bool HasEnglishDescription(AdminShellV20.ConceptDescription decription)
        {
            return decription.IEC61360Content.definition["EN"] != null;
        }

        private static void SetDescription(
            AttributeType smAttChild, 
            AdminShellV20.ConceptDescription decription)
        {
            smAttChild.Description = decription.IEC61360Content.definition["EN"].ToString();
        }

        private static AdminShellV20.ConceptDescription GetMatchingDescription(
            AdminShellV20.ListOfConceptDescriptions descriptions, 
            AdminShellV20.SubmodelElementWrapper submodelElement)
        {
            return descriptions.SingleOrDefault(IsMatching);

            bool IsMatching(AdminShellV20.ConceptDescription toCheck)
            {
                return "[ConceptDescription, Local, " 
                    + toCheck.identification.idType.ToString() 
                    + ", " 
                    + toCheck.identification.id.ToString() 
                    + "]" 
                    == submodelElement.submodelElement.semanticId.ToString();
            }
        }
    }
}
