namespace Microsoft.PythonTools.Options {
    partial class PythonGeneralOptionsControl {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && (components != null)) {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent() {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PythonGeneralOptionsControl));
            this.tableLayoutPanel3 = new System.Windows.Forms.TableLayoutPanel();
            this._showOutputWindowForVirtualEnvCreate = new System.Windows.Forms.CheckBox();
            this._showOutputWindowForPackageInstallation = new System.Windows.Forms.CheckBox();
            this._autoAnalysis = new System.Windows.Forms.CheckBox();
            this._updateSearchPathsForLinkedFiles = new System.Windows.Forms.CheckBox();
            this._indentationInconsistentLabel = new System.Windows.Forms.Label();
            this._indentationInconsistentCombo = new System.Windows.Forms.ComboBox();
            this._surveyNewsCheckLabel = new System.Windows.Forms.Label();
            this._surveyNewsCheckCombo = new System.Windows.Forms.ComboBox();
            this._elevatePip = new System.Windows.Forms.CheckBox();
            this._elevateEasyInstall = new System.Windows.Forms.CheckBox();
            this._unresolvedImportWarning = new System.Windows.Forms.CheckBox();
            this._clearGlobalPythonPath = new System.Windows.Forms.CheckBox();
            this._resetSuppressDialog = new System.Windows.Forms.Button();
            this.tableLayoutPanel3.SuspendLayout();
            this.SuspendLayout();
            // 
            // tableLayoutPanel3
            // 
            resources.ApplyResources(this.tableLayoutPanel3, "tableLayoutPanel3");
            this.tableLayoutPanel3.Controls.Add(this._showOutputWindowForVirtualEnvCreate, 0, 0);
            this.tableLayoutPanel3.Controls.Add(this._showOutputWindowForPackageInstallation, 0, 1);
            this.tableLayoutPanel3.Controls.Add(this._autoAnalysis, 0, 4);
            this.tableLayoutPanel3.Controls.Add(this._updateSearchPathsForLinkedFiles, 0, 6);
            this.tableLayoutPanel3.Controls.Add(this._indentationInconsistentLabel, 0, 8);
            this.tableLayoutPanel3.Controls.Add(this._indentationInconsistentCombo, 1, 8);
            this.tableLayoutPanel3.Controls.Add(this._surveyNewsCheckLabel, 0, 9);
            this.tableLayoutPanel3.Controls.Add(this._surveyNewsCheckCombo, 1, 9);
            this.tableLayoutPanel3.Controls.Add(this._elevatePip, 0, 2);
            this.tableLayoutPanel3.Controls.Add(this._elevateEasyInstall, 0, 3);
            this.tableLayoutPanel3.Controls.Add(this._unresolvedImportWarning, 0, 7);
            this.tableLayoutPanel3.Controls.Add(this._clearGlobalPythonPath, 0, 5);
            this.tableLayoutPanel3.Controls.Add(this._resetSuppressDialog, 0, 10);
            this.tableLayoutPanel3.Name = "tableLayoutPanel3";
            // 
            // _showOutputWindowForVirtualEnvCreate
            // 
            resources.ApplyResources(this._showOutputWindowForVirtualEnvCreate, "_showOutputWindowForVirtualEnvCreate");
            this.tableLayoutPanel3.SetColumnSpan(this._showOutputWindowForVirtualEnvCreate, 2);
            this._showOutputWindowForVirtualEnvCreate.Name = "_showOutputWindowForVirtualEnvCreate";
            this._showOutputWindowForVirtualEnvCreate.UseVisualStyleBackColor = true;
            // 
            // _showOutputWindowForPackageInstallation
            // 
            resources.ApplyResources(this._showOutputWindowForPackageInstallation, "_showOutputWindowForPackageInstallation");
            this.tableLayoutPanel3.SetColumnSpan(this._showOutputWindowForPackageInstallation, 2);
            this._showOutputWindowForPackageInstallation.Name = "_showOutputWindowForPackageInstallation";
            this._showOutputWindowForPackageInstallation.UseVisualStyleBackColor = true;
            // 
            // _autoAnalysis
            // 
            resources.ApplyResources(this._autoAnalysis, "_autoAnalysis");
            this.tableLayoutPanel3.SetColumnSpan(this._autoAnalysis, 2);
            this._autoAnalysis.Name = "_autoAnalysis";
            this._autoAnalysis.UseVisualStyleBackColor = true;
            // 
            // _updateSearchPathsForLinkedFiles
            // 
            resources.ApplyResources(this._updateSearchPathsForLinkedFiles, "_updateSearchPathsForLinkedFiles");
            this.tableLayoutPanel3.SetColumnSpan(this._updateSearchPathsForLinkedFiles, 2);
            this._updateSearchPathsForLinkedFiles.Name = "_updateSearchPathsForLinkedFiles";
            this._updateSearchPathsForLinkedFiles.UseVisualStyleBackColor = true;
            // 
            // _indentationInconsistentLabel
            // 
            resources.ApplyResources(this._indentationInconsistentLabel, "_indentationInconsistentLabel");
            this._indentationInconsistentLabel.AutoEllipsis = true;
            this._indentationInconsistentLabel.Name = "_indentationInconsistentLabel";
            // 
            // _indentationInconsistentCombo
            // 
            resources.ApplyResources(this._indentationInconsistentCombo, "_indentationInconsistentCombo");
            this._indentationInconsistentCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._indentationInconsistentCombo.FormattingEnabled = true;
            this._indentationInconsistentCombo.Items.AddRange(new object[] {
            resources.GetString("_indentationInconsistentCombo.Items"),
            resources.GetString("_indentationInconsistentCombo.Items1"),
            resources.GetString("_indentationInconsistentCombo.Items2")});
            this._indentationInconsistentCombo.Name = "_indentationInconsistentCombo";
            // 
            // _surveyNewsCheckLabel
            // 
            resources.ApplyResources(this._surveyNewsCheckLabel, "_surveyNewsCheckLabel");
            this._surveyNewsCheckLabel.Name = "_surveyNewsCheckLabel";
            // 
            // _surveyNewsCheckCombo
            // 
            resources.ApplyResources(this._surveyNewsCheckCombo, "_surveyNewsCheckCombo");
            this._surveyNewsCheckCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this._surveyNewsCheckCombo.DropDownWidth = 172;
            this._surveyNewsCheckCombo.FormattingEnabled = true;
            this._surveyNewsCheckCombo.Items.AddRange(new object[] {
            resources.GetString("_surveyNewsCheckCombo.Items"),
            resources.GetString("_surveyNewsCheckCombo.Items1"),
            resources.GetString("_surveyNewsCheckCombo.Items2"),
            resources.GetString("_surveyNewsCheckCombo.Items3")});
            this._surveyNewsCheckCombo.Name = "_surveyNewsCheckCombo";
            // 
            // _elevatePip
            // 
            resources.ApplyResources(this._elevatePip, "_elevatePip");
            this.tableLayoutPanel3.SetColumnSpan(this._elevatePip, 2);
            this._elevatePip.Name = "_elevatePip";
            this._elevatePip.UseVisualStyleBackColor = true;
            // 
            // _elevateEasyInstall
            // 
            resources.ApplyResources(this._elevateEasyInstall, "_elevateEasyInstall");
            this.tableLayoutPanel3.SetColumnSpan(this._elevateEasyInstall, 2);
            this._elevateEasyInstall.Name = "_elevateEasyInstall";
            this._elevateEasyInstall.UseVisualStyleBackColor = true;
            // 
            // _unresolvedImportWarning
            // 
            resources.ApplyResources(this._unresolvedImportWarning, "_unresolvedImportWarning");
            this.tableLayoutPanel3.SetColumnSpan(this._unresolvedImportWarning, 2);
            this._unresolvedImportWarning.Name = "_unresolvedImportWarning";
            this._unresolvedImportWarning.UseVisualStyleBackColor = true;
            // 
            // _clearGlobalPythonPath
            // 
            resources.ApplyResources(this._clearGlobalPythonPath, "_clearGlobalPythonPath");
            this.tableLayoutPanel3.SetColumnSpan(this._clearGlobalPythonPath, 2);
            this._clearGlobalPythonPath.Name = "_clearGlobalPythonPath";
            this._clearGlobalPythonPath.UseVisualStyleBackColor = true;
            // 
            // _resetSuppressDialog
            // 
            resources.ApplyResources(this._resetSuppressDialog, "_resetSuppressDialog");
            this.tableLayoutPanel3.SetColumnSpan(this._resetSuppressDialog, 2);
            this._resetSuppressDialog.Name = "_resetSuppressDialog";
            this._resetSuppressDialog.UseVisualStyleBackColor = true;
            this._resetSuppressDialog.Click += new System.EventHandler(this._resetSuppressDialog_Click);
            // 
            // PythonGeneralOptionsControl
            // 
            resources.ApplyResources(this, "$this");
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.tableLayoutPanel3);
            this.Name = "PythonGeneralOptionsControl";
            this.tableLayoutPanel3.ResumeLayout(false);
            this.tableLayoutPanel3.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel3;
        private System.Windows.Forms.Label _surveyNewsCheckLabel;
        private System.Windows.Forms.ComboBox _surveyNewsCheckCombo;
        private System.Windows.Forms.CheckBox _showOutputWindowForVirtualEnvCreate;
        private System.Windows.Forms.CheckBox _showOutputWindowForPackageInstallation;
        private System.Windows.Forms.CheckBox _autoAnalysis;
        private System.Windows.Forms.CheckBox _updateSearchPathsForLinkedFiles;
        private System.Windows.Forms.Label _indentationInconsistentLabel;
        private System.Windows.Forms.ComboBox _indentationInconsistentCombo;
        private System.Windows.Forms.CheckBox _elevatePip;
        private System.Windows.Forms.CheckBox _elevateEasyInstall;
        private System.Windows.Forms.CheckBox _unresolvedImportWarning;
        private System.Windows.Forms.CheckBox _clearGlobalPythonPath;
        private System.Windows.Forms.Button _resetSuppressDialog;
    }
}
