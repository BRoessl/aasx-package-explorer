/*
Copyright (c) 2018-2019 Festo AG & Co. KG <https://www.festo.com/net/de_de/Forms/web/contact_international>
Author: Michael Hoffmeister

This source code is licensed under the Apache License 2.0 (see LICENSE.txt).

This source code may use other Open Source software components (see LICENSE.txt).
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using AasxIntegrationBase;
using AasxPackageLogic;
using AasxWpfControlLibrary;
using AdminShellNS;
using Newtonsoft.Json;

namespace AasxPackageExplorer
{
    public partial class SelectAasEntityFlyout : UserControl, IFlyoutControl
    {
        public event IFlyoutControlClosed ControlClosed;

        // TODO (MIHO, 21-12-2020): make DiaData non-Nullable
        public AnyUiDialogueDataSelectAasEntity DiaData = new AnyUiDialogueDataSelectAasEntity();

        PackageCentral packages = null;

        public SelectAasEntityFlyout(
            PackageCentral packages,
            PackageCentral.Selector? selector = null,
            string filter = null)
        {
            InitializeComponent();
            this.packages = packages;
            if (selector.HasValue)
                DiaData.Selector = selector.Value;
            if (filter != null)
                DiaData.Filter = filter;
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            DisplayElements.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));

            // fill combo box
            ComboBoxFilter.Items.Add("All");
            foreach (var x in AdminShell.Key.KeyElements)
                ComboBoxFilter.Items.Add(x);

            // select an item
            if (DiaData.Filter != null)
                foreach (var x in ComboBoxFilter.Items)
                    if (x.ToString().Trim().ToLower() == DiaData.Filter.Trim().ToLower())
                    {
                        ComboBoxFilter.SelectedItem = x;
                        break;
                    }

            // fill contents
            FilterFor(DiaData.Filter);
        }

        //
        // Outer
        //

        public void ControlStart()
        {
        }

        public void ControlPreviewKeyDown(KeyEventArgs e)
        {
        }

        //
        // Mechanics
        //

        private void ComboBoxFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender == ComboBoxFilter)
            {
                DiaData.Filter = ComboBoxFilter.SelectedItem.ToString();
                if (DiaData.Filter == "All")
                    DiaData.Filter = null;
                // fill contents
                FilterFor(DiaData.Filter);
            }
        }

        private AdminShell.KeyList BuildKeyListToTop(
            VisualElementGeneric visElem,
            bool includeAas = false)
        {
            // access
            if (visElem == null)
                return null;

            // prepare result
            var res = new AdminShell.KeyList();
            var ve = visElem;
            while (ve != null)
            {
                if (ve is VisualElementSubmodelRef)
                {
                    // import special case, as Submodel ref is important part of the chain!
                    var elem = ve as VisualElementSubmodelRef;
                    if (elem.theSubmodel != null)
                        res.Insert(
                            0,
                            AdminShell.Key.CreateNew(
                                elem.theSubmodel.GetElementName(), true,
                                elem.theSubmodel.identification.idType,
                                elem.theSubmodel.identification.id));

                    // include aas
                    if (includeAas && ve.Parent is VisualElementAdminShell veAas
                        && veAas.theAas?.identification != null)
                    {
                        res.Insert(
                            0,
                            AdminShell.Key.CreateNew(
                                AdminShell.Key.AAS, true,
                                veAas.theAas.identification.idType,
                                veAas.theAas.identification.id));
                    }

                    break;
                }
                else
                if (ve.GetMainDataObject() is AdminShell.Identifiable)
                {
                    // a Identifiable will terminate the list of keys
                    var data = ve.GetMainDataObject() as AdminShell.Identifiable;
                    if (data != null)
                        res.Insert(
                            0,
                            AdminShell.Key.CreateNew(
                                data.GetElementName(), true, data.identification.idType, data.identification.id));
                    break;
                }
                else
                if (ve.GetMainDataObject() is AdminShell.Referable)
                {
                    // add a key and go up ..
                    var data = ve.GetMainDataObject() as AdminShell.Referable;
                    if (data != null)
                        res.Insert(
                            0,
                            AdminShell.Key.CreateNew(data.GetElementName(), true, "IdShort", data.idShort));
                }
                else
                // uups!
                { }
                // need to go up
                ve = ve.Parent;
            }

            return res;
        }

        private bool PrepareResult()
        {
            // access
            if (DisplayElements == null || DisplayElements.SelectedItem == null)
                return false;
            var si = DisplayElements.SelectedItem;
            var siMdo = si.GetMainDataObject();

            // already one result
            DiaData.ResultVisualElement = si;

            //
            // Referable
            //
            if (siMdo is AdminShell.Referable dataRef)
            {
                // check if a valuable item was selected
                var elemname = dataRef.GetElementName();
                var fullFilter = ApplyFullFilterString(DiaData.Filter);
                if (fullFilter != null && !(fullFilter.IndexOf(elemname + " ", StringComparison.Ordinal) >= 0))
                    return false;

                // ok, prepare list of keys
                DiaData.ResultKeys = BuildKeyListToTop(si);
                return true;
            }

            //
            // other special cases
            //
            if (siMdo is AdminShell.SubmodelRef smref &&
                    (DiaData.Filter == null ||
                        ApplyFullFilterString(DiaData.Filter)
                            .ToLower().IndexOf("submodelref ", StringComparison.Ordinal) >= 0))
            {
                DiaData.ResultKeys = new AdminShell.KeyList();
                DiaData.ResultKeys.AddRange(smref.Keys);
                return true;
            }

            if (si is VisualElementPluginExtension vepe)
            {
                // get main data object of the parent of the plug in ..
                var parentMdo = vepe.Parent.GetMainDataObject();
                if (parentMdo != null)
                {
                    // safe to return a list for the parent ..
                    // (include AAS, as this is important to plug-ins)
                    DiaData.ResultKeys = BuildKeyListToTop(si, includeAas: true);

                    // .. enriched by a last element
                    DiaData.ResultKeys.Add(new AdminShell.Key(AdminShell.Key.FragmentReference, true,
                        AdminShell.Key.Custom, "Plugin:" + vepe.theExt.Tag));

                    // ok
                    return true;
                }
            }

            // uups
            return false;
        }

        private void ButtonSelect_Click(object sender, RoutedEventArgs e)
        {
            if (PrepareResult())
            {
                DiaData.Result = true;
                ControlClosed?.Invoke();
            }
        }

        private string ApplyFullFilterString(string filter)
        {
            if (filter == null)
                return null;
            var res = filter;
            if (res.Trim().ToLower() == "submodelelement")
                foreach (var s in AdminShell.Key.SubmodelElements)
                    res += " " + s + " ";
            return " " + res + " ";
        }

        private void FilterFor(string filter)
        {
            filter = ApplyFullFilterString(filter);
            DisplayElements.RebuildAasxElements(packages, DiaData.Selector, true, filter);
        }

        private void DisplayElements_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PrepareResult())
            {
                DiaData.Result = true;
                ControlClosed?.Invoke();
            }
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            DiaData.Result = false;
            ControlClosed?.Invoke();
        }
    }
}
